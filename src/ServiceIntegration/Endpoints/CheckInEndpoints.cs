using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceIntegration.Core.Abstractions;
using ServiceIntegration.Core.Contracts;
using ServiceIntegration.Infrastructure.Configuration;
using ServiceIntegration.Infrastructure.Elastic;
using System.Text;
using System.Text.Json;

namespace ServiceIntegration.Endpoints;

public static class CheckInEndpoints
{
    public static IEndpointRouteBuilder MapCheckInEndpoints(this IEndpointRouteBuilder app)
    {
        // Receive CHECKIN event (PMS -> Integration)
        app.MapPost("/events/checkin", async (
            [FromBody] EventEnvelope envelope,
            IIntegrationQueue queue,
            IElasticLogger elastic,
            IOptions<TigerOptions> tigerOpt) =>
        {
            if (string.IsNullOrWhiteSpace(envelope.HotelId)) return Results.BadRequest("hotelId is required");
            if (string.IsNullOrWhiteSpace(envelope.EventId)) return Results.BadRequest("eventId is required");

            envelope.EventType = "CHECKIN";
            var correlationId = Guid.NewGuid().ToString("N"); // Generate a new correlationId for this request; in production, client có thể gửi kèm correlationId riêng

            // Log ingress — scoped entry cho request này
            var log = new ElasticLogEntry(elastic)
                .SetHotelId(envelope.HotelId)
                .SetEventId(envelope.EventId)
                .SetCorrelationId(correlationId)
                .SetEventType("CHECKIN");

            await log.SetAction("CHECKIN_RECEIVED").InfoAsync("Nhận request từ client");

            // Enqueue only payload bytes (handlers parse payload)
            var payloadJson = JsonSerializer.Serialize(envelope.Payload);
            var body = Encoding.UTF8.GetBytes(payloadJson);

            var headers = new MessageHeaders();
            headers.Set("x-hotel-id",       envelope.HotelId);
            headers.Set("x-event-id",       envelope.EventId);
            headers.Set("x-event-type",     envelope.EventType);
            headers.Set("x-correlation-id", correlationId);
            headers.Set("x-attempt",        0);

            // Demo: use global wsuserkey from config; production: load by HotelId (DB/config service)
            var wsuserkey = tigerOpt.Value.WsUserKey;
            if (!string.IsNullOrWhiteSpace(wsuserkey))
                headers.Set("x-wsuserkey", wsuserkey);

            try
            {
                await queue.PublishAsync(body, headers, CancellationToken.None);
                await log.SetAction("CHECKIN_QUEUED").InfoAsync("Đã đưa vào queue thành công");
            }
            catch (Exception ex)
            {
                await log.SetAction("CHECKIN_QUEUE_FAILED").ErrorAsync("Lỗi khi publish lên queue", ex);
                return Results.StatusCode(503);
            }

            return Results.Ok(new { status = "QUEUED", envelope.EventId, envelope.HotelId, correlationId });
        })
        .WithName("CheckIn");

        return app;
    }
}
