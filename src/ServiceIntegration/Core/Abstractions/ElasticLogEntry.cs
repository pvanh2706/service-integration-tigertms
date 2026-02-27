using System.Diagnostics;
using System.Text.Json.Serialization;

namespace ServiceIntegration.Core.Abstractions;

/// <summary>
/// Scoped typed model + fluent builder ghi log lên Elasticsearch cho một message cụ thể.
/// Tạo mới cho mỗi message - không dùng static/shared state.
/// Tất cả field là typed property: đổi tên property → compiler báo lỗi ngay.
/// ES field name được ấn định qua [JsonPropertyName] - không phụ thuộc tên biến.
/// <para>Cách dùng:</para>
/// <code>
/// var log = ElasticLogEntry.FromContext(_elastic, ctx).SetEventType("CHECKIN");
/// await log.SetAttempt(attempt).InfoAsync("Bắt đầu xử lý", ct);
/// var res  = await log.TimedAsync("Gọi Tiger", () => _tiger.SendAsync(...), ct);
/// </code>
/// </summary>
public sealed class ElasticLogEntry
{
    [JsonIgnore]
    private readonly IElasticLogger _elastic;

    public ElasticLogEntry(IElasticLogger elastic) => _elastic = elastic;

    // ── Factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Tạo entry với fields chung từ EventContext.
    /// Mapping hotelId/eventId/correlationId chỉ nằm ở đây - đổi là đổi một chỗ.
    /// </summary>
    public static ElasticLogEntry FromContext(IElasticLogger elastic, EventContext ctx)
        => new ElasticLogEntry(elastic)
            .SetHotelId(ctx.HotelId)
            .SetEventId(ctx.EventId)
            .SetCorrelationId(ctx.CorrelationId);

    // ── Fixed metadata (luôn có mặt trong mọi document) ────────────────────

    [JsonPropertyName("@timestamp")]
    public string? Timestamp { get; private set; }

    [JsonPropertyName("level")]
    public string? Level { get; private set; }

    [JsonPropertyName("message")]
    public string? Message { get; private set; }

    [JsonPropertyName("service")]
    public string Service { get; } = "ServiceIntegration.TigerTMS";

    // ── Common event fields ──────────────────────────────────────────
    /// <summary>
    /// Mã khách sạn - có thể dùng để phân sharding index, filter log theo hotel, v.v.
    /// Nên có trong mọi log liên quan đến event để tiện filter/trace khi debugging.
    /// Nếu có nhiều event liên quan đến cùng một hotelId/eventId thì sẽ dễ dàng trace theo hotelId/eventId/correlationId. Nếu thiếu hotelId thì việc trace sẽ khó khăn hơn nhiều (phải dựa vào correlationId nếu có, hoặc phải dò theo eventId nhưng có thể không unique). Do đó, tốt nhất là nên có hotelId trong mọi log liên quan đến event processing, kể cả log lỗi (nếu có thể lấy được hotelId). Nếu không có hotelId thì ít nhất cũng nên có eventId để trace theo eventId, nhưng sẽ khó hơn nếu có nhiều event cùng eventId. CorrelationId thì tùy client gửi vào thế nào, không đảm bảo có mặt trong mọi log.
    /// Nếu log liên quan đến một event cụ thể thì nên có hotelId/eventId/correlationId để dễ trace. Nếu log không liên quan đến event nào cụ thể (ví dụ lỗi chung của service) thì có thể không cần hotelId/eventId nhưng vẫn nên có correlationId nếu có thể lấy được, để trace theo correlationId nếu client gửi vào.
    /// Được set qua SetHotelId() để đảm bảo không bị bỏ trống, tránh log thiếu hotelId
    /// Ánh xạ field name trong ES qua [JsonPropertyName] để không phụ thuộc tên biến trong code, dễ refactor.
    /// </summary>
    [JsonPropertyName("hotelId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HotelId { get; private set; }

    [JsonPropertyName("eventId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventId { get; private set; }

    [JsonPropertyName("correlationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; private set; }

    [JsonPropertyName("eventType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventType { get; private set; }

    [JsonPropertyName("attempt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Attempt { get; private set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; private set; }

    [JsonPropertyName("reservationNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReservationNumber { get; private set; }

    [JsonPropertyName("room")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Room { get; private set; }

    [JsonPropertyName("started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartedAt { get; private set; }

    [JsonPropertyName("end_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EndAt { get; private set; }

    [JsonPropertyName("duration_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; private set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; private set; }

    [JsonPropertyName("tiger_response")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TigerRawResponse { get; private set; }

    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; private set; }

    [JsonPropertyName("exception_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExceptionType { get; private set; }

    [JsonPropertyName("exception_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExceptionMessage { get; private set; }

    [JsonPropertyName("exception_stack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExceptionStack { get; private set; }

    // ── Typed setters ─────────────────────────────────────────────────────

    public ElasticLogEntry SetHotelId(string? v)      { if (!string.IsNullOrEmpty(v)) HotelId      = v; return this; }
    public ElasticLogEntry SetEventId(string? v)       { if (!string.IsNullOrEmpty(v)) EventId       = v; return this; }
    public ElasticLogEntry SetCorrelationId(string? v) { if (!string.IsNullOrEmpty(v)) CorrelationId = v; return this; }
    public ElasticLogEntry SetEventType(string? v)     { if (!string.IsNullOrEmpty(v)) EventType     = v; return this; }
    public ElasticLogEntry SetAttempt(int attempt)     { Attempt = attempt;                                return this; }
    public ElasticLogEntry SetReason(string? v)        { if (!string.IsNullOrEmpty(v)) Reason        = v; return this; }

    public ElasticLogEntry SetReservation(string? reservationNumber, string? room)
    {
        if (!string.IsNullOrEmpty(reservationNumber)) ReservationNumber = reservationNumber;
        if (!string.IsNullOrEmpty(room))              Room              = room;
        return this;
    }

    public ElasticLogEntry SetAction(string? v)        { if (!string.IsNullOrEmpty(v)) Action        = v; return this; }

    public ElasticLogEntry SetTigerResponse(string? raw)
    {
        if (!string.IsNullOrEmpty(raw))
            TigerRawResponse = raw.Length > 500 ? raw[..500] : raw; // cắt tránh document quá lớn
        return this;
    }

    public ElasticLogEntry SetDuration(DateTimeOffset startedAt, long elapsedMs)
    {
        StartedAt  = startedAt.ToString("o"); // ISO 8601 format
        DurationMs = elapsedMs;
        EndAt = DateTimeOffset.UtcNow.ToString("o"); // ISO 8601 format
        return this;
    }

    // ── Log methods ───────────────────────────────────────────────────────

    public Task InfoAsync(string message, CancellationToken ct = default)
    {
        Timestamp = DateTimeOffset.UtcNow.ToString("o");
        Level     = "INFO";
        Message   = message;
        return _elastic.PostAsync(this, ct);
    }

    public Task WarnAsync(string message, CancellationToken ct = default)
    {
        Timestamp = DateTimeOffset.UtcNow.ToString("o");
        Level     = "WARN";
        Message   = message;
        return _elastic.PostAsync(this, ct);
    }

    public Task ErrorAsync(string message, Exception? ex = null, CancellationToken ct = default)
    {
        Timestamp        = DateTimeOffset.UtcNow.ToString("o");
        Level            = "ERROR";
        Message          = message;
        ExceptionType    = ex?.GetType().FullName;
        ExceptionMessage = ex?.Message;
        ExceptionStack   = ex?.StackTrace;
        return _elastic.PostAsync(this, ct);
    }

    /// <summary>
    /// Chạy <paramref name="operation"/>, tự động đo thời gian.
    /// Thành công → INFO; Exception → ERROR (re-throw để caller xử lý).
    /// </summary>
    public async Task<T> TimedAsync<T>(
        string message,
        Func<Task<T>> operation,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            sw.Stop();
            Status = "success";
            SetDuration(startedAt, sw.ElapsedMilliseconds);
            await InfoAsync(message, ct);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            Status = "error";
            SetDuration(startedAt, sw.ElapsedMilliseconds);
            await ErrorAsync(message, ex, ct);
            throw;
        }
    }
}