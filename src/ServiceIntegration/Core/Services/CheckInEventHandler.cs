using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServiceIntegration.Core.Abstractions;
using ServiceIntegration.Core.Contracts;
using ServiceIntegration.Infrastructure.RabbitMq;
using ServiceIntegration.Infrastructure.TigerTms;

namespace ServiceIntegration.Core.Services;

public sealed class CheckInEventHandler : IEventHandler
{
    public string EventType => "CHECKIN";

    private readonly ILogger<CheckInEventHandler> _logger;
    private readonly IElasticLogger _elastic;
    private readonly ITigerClient _tiger;
    private readonly IPmsCallbackClient _pms;
    private readonly IIdempotencyStore _idempo;
    private readonly RetryRouter _retryRouter;
    private readonly RabbitPublisher _publisher;

    public CheckInEventHandler(
        ILogger<CheckInEventHandler> logger,
        IElasticLogger elastic,
        ITigerClient tiger,
        IPmsCallbackClient pms,
        IIdempotencyStore idempo,
        RetryRouter retryRouter,
        RabbitPublisher publisher)
    {
        _logger = logger;
        _elastic = elastic;
        _tiger = tiger;
        _pms = pms;
        _idempo = idempo;
        _retryRouter = retryRouter;
        _publisher = publisher;
    }

    public async Task HandleAsync(EventContext ctx, CancellationToken ct)
    {
        // Scoped log entry — khai báo ngoài try để catch có thể dùng
        var log = ElasticLogEntry.FromContext(_elastic, ctx)
                      .SetEventType("CHECKIN");
        try
        {
            if (_idempo.SeenRecently(ctx.HotelId, ctx.EventId))
            {
                _logger.LogWarning("DUPLICATE seen recently -> ACK hotelId={HotelId} eventId={EventId}", ctx.HotelId, ctx.EventId);
                await log.WarnAsync("DUPLICATE: event đã xử lý gần đây, bỏ qua", ct);
                await ctx.Ack();
                return;
            }

            var attempt = ctx.Headers.GetInt("x-attempt", 0);
            log.SetAttempt(attempt);

            _logger.LogInformation("HANDLE CHECKIN hotelId={HotelId} eventId={EventId} attempt={Attempt}",
                ctx.HotelId, ctx.EventId, attempt);
            await log.InfoAsync("CHECKIN: bắt đầu xử lý", ct);

            CheckInPayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<CheckInPayload>(
                    ctx.Body.Span,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? throw new Exception("payload is null");
                _logger.LogInformation("Payload: resno={Resno}, room={Room}, viewbill={ViewBill}, expressco={ExpressCo}",
                    payload.ReservationNumber, payload.Room, payload.ViewBill, payload.ExpressCo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid payload -> DEAD");
                await log.ErrorAsync("CHECKIN: payload không hợp lệ -> chuyển DEAD", ex, ct);
                await Republish(ctx, attempt, "Invalid payload", forceDead: true);
                await ctx.Ack();
                return;
            }

            var wsuserkey = ctx.Headers.GetString("x-wsuserkey");
            if (string.IsNullOrWhiteSpace(wsuserkey))
            {
                _logger.LogWarning("Missing wsuserkey -> retry");
                await log.WarnAsync("CHECKIN: thiếu wsuserkey -> retry", ct);
                await Republish(ctx, attempt, "Missing wsuserkey");
                await ctx.Ack();
                return;
            }

            var optional = new Dictionary<string, string?>()
            {
                ["title"]     = payload.Title,
                ["last"]      = payload.Last,
                ["first"]     = payload.First,
                ["guestid"]   = payload.GuestId?.ToString(),
                ["lang"]      = payload.Lang,
                ["group"]     = payload.Group,
                ["vip"]       = payload.Vip,
                ["email"]     = payload.Email,
                ["mobile"]    = payload.Mobile,
                ["arrival"]   = payload.Arrival,
                ["departure"] = payload.Departure,
                ["tv"]        = payload.Tv,
                ["minibar"]   = payload.Minibar,
                ["viewbill"]  = payload.ViewBill.HasValue  ? (payload.ViewBill.Value  ? "True" : "False") : null,
                ["expressco"] = payload.ExpressCo.HasValue ? (payload.ExpressCo.Value ? "True" : "False") : null,
            };

            var innerXml = TigerSoapBuilder.BuildCheckInInnerXml(
                resno: payload.ReservationNumber,
                site: payload.Site,
                room: payload.Room,
                wsuserkey: wsuserkey,
                optionalNodes: optional
            );

            // Gọi Tiger — tự động đo thời gian, ghi started_at + duration_ms + status vào ES
            _logger.LogInformation("==>> SENT_TO_TIGER hotelId={HotelId} eventId={EventId}", ctx.HotelId, ctx.EventId);
            var tigerRes = await log
                .SetReservation(payload.ReservationNumber, payload.Room)
                .TimedAsync("CHECKIN: gọi Tiger TMS",
                    () => _tiger.SendCheckInAsync(innerXml, ct), ct);

            log.SetTigerResponse(tigerRes.RawResponse);

            if (!tigerRes.IsSuccess)
            {
                _logger.LogWarning("TIGER_FAILED eventId={EventId} reason={Reason}", ctx.EventId, tigerRes.FailureReason);
                await log.SetReason(tigerRes.FailureReason).WarnAsync("CHECKIN: Tiger phản hồi lỗi -> retry", ct);
                await Republish(ctx, attempt, tigerRes.FailureReason ?? "Tiger failed");
                await ctx.Ack();
                return;
            }

            // Gọi callback PMS — đo thời gian
            var ok = await log.TimedAsync("CHECKIN: gọi callback PMS",
                async () =>
                {
                    var result = await _pms.NotifyAsync(new PmsCallbackRequest(
                        ctx.HotelId,
                        ctx.EventId,
                        ctx.Headers.GetString("x-event-type", "CHECKIN"),
                        TigerStatus: "SUCCESS",
                        TigerReason: null,
                        CorrelationId: ctx.CorrelationId
                    ), ct);
                    return result;
                }, ct);

            if (!ok)
            {
                _logger.LogWarning("CALLBACK_FAIL eventId={EventId}", ctx.EventId);
                await log.WarnAsync("CHECKIN: gọi callback PMS thất bại -> retry", ct);
                await Republish(ctx, attempt, "Callback PMS failed");
                await ctx.Ack();
                return;
            }

            _idempo.MarkSeen(ctx.HotelId, ctx.EventId, TimeSpan.FromHours(6));
            _logger.LogInformation("DONE CHECKIN -> ACK eventId={EventId}", ctx.EventId);
            await log.InfoAsync("CHECKIN: xử lý thành công -> ACK", ct);
            await ctx.Ack();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UNHANDLED exception in CHECKIN handler hotelId={HotelId} eventId={EventId}",
                ctx.HotelId, ctx.EventId);
            await log.ErrorAsync("CHECKIN: exception không xử lý được -> chuyển DEAD", ex, ct);
            try
            {
                var attempt = ctx.Headers.GetInt("x-attempt", 0);
                await Republish(ctx, attempt, $"Unhandled: {ex.Message}", forceDead: true);
                await ctx.Ack();
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to dead-letter message after unhandled exception hotelId={HotelId} eventId={EventId}",
                    ctx.HotelId, ctx.EventId);
            }
        }
    }

    private async Task Republish(EventContext ctx, int attempt, string reason, bool forceDead = false)
    {
        var nextAttempt = attempt + 1;

        var headers = ctx.Headers.AsReadOnly().ToDictionary(kv => kv.Key, kv => kv.Value);
        headers["x-attempt"] = nextAttempt;
        headers["x-last-error"] = reason;

        var route = forceDead ? RetryRoute.Dead : _retryRouter.Decide(attempt).Route;
        var routingKey = _publisher.RoutingKeyForRetry(route);

        await _publisher.PublishToRetryAsync(ctx.Body, headers, routingKey);
    }
}
