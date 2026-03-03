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

    /// <summary>
    /// Thời điểm ghi log theo UTC, định dạng ISO 8601 (ví dụ: <c>2026-03-01T08:00:00.000Z</c>).
    /// Là field chuẩn của Elasticsearch, dùng để sort và filter theo thời gian.
    /// </summary>
    [JsonPropertyName("@timestamp")]
    public string? Timestamp { get; private set; }

    /// <summary>
    /// Mức độ log: <c>INFO</c>, <c>WARN</c>, hoặc <c>ERROR</c>.
    /// Được set tự động bởi <see cref="InfoAsync"/>, <see cref="WarnAsync"/>, <see cref="ErrorAsync"/>.
    /// </summary>
    [JsonPropertyName("level")]
    public string? Level { get; private set; }

    /// <summary>
    /// Nội dung mô tả sự kiện hoặc lỗi xảy ra tại thời điểm ghi log.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; private set; }

    /// <summary>
    /// Tên service cố định — luôn là <c>ServiceIntegration.TigerTMS</c>.
    /// Dùng để phân biệt log từ service này với các service khác trong cùng Elasticsearch cluster.
    /// </summary>
    [JsonPropertyName("service")]
    public string Service { get; } = "ServiceIntegration.TigerTMS";

    // ── Common event fields ──────────────────────────────────────────
    /// <summary>
    /// Mã khách sạn — dùng để filter/trace log theo hotel, hỗ trợ sharding index trong Elasticsearch.
    /// Nên có trong mọi log liên quan đến event processing (kể cả log lỗi) để tiện debug.
    /// <para>Chiến lược trace: có <c>hotelId</c> + <c>eventId</c> → trace dễ nhất;  
    /// chỉ có <c>correlationId</c> → vẫn trace được nếu client gửi đúng;  
    /// chỉ có <c>eventId</c> → khó hơn vì có thể không unique.</para>
    /// Set qua <see cref="SetHotelId"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("hotelId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HotelId { get; private set; }

    /// <summary>
    /// ID duy nhất của event — dùng để trace một event cụ thể xuyên suốt toàn bộ pipeline xử lý.
    /// Set qua <see cref="SetEventId"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("eventId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventId { get; private set; }

    /// <summary>
    /// ID tương quan do client gửi vào — liên kết các log thuộc cùng một request flow.
    /// Không đảm bảo luôn có mặt (tùy client có gửi hay không).
    /// Set qua <see cref="SetCorrelationId"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("correlationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Loại sự kiện được xử lý, ví dụ: <c>CHECKIN</c>, <c>CHECKOUT</c>, <c>RESERVATION</c>.
    /// Dùng để phân loại và filter log theo nghiệp vụ.
    /// Set qua <see cref="SetEventType"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("eventType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventType { get; private set; }

    /// <summary>
    /// Số lần thử xử lý event, bắt đầu từ 1.
    /// Dùng để theo dõi retry logic — giá trị lớn hơn 1 có nghĩa là đã được retry.
    /// Set qua <see cref="SetAttempt"/>.
    /// </summary>
    [JsonPropertyName("attempt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Attempt { get; private set; }

    /// <summary>
    /// Lý do thất bại hoặc ghi chú bổ sung khi route sang dead-letter/retry queue.
    /// Set qua <see cref="SetReason"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; private set; }

    /// <summary>
    /// Số đặt phòng từ PMS — dùng để liên kết log với reservation cụ thể.
    /// Set qua <see cref="SetReservation"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("reservationNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReservationNumber { get; private set; }

    /// <summary>
    /// Số phòng khách — dùng để liên kết log với phòng cụ thể trong khách sạn.
    /// Set qua <see cref="SetReservation"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("room")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Room { get; private set; }

    /// <summary>
    /// Thời điểm bắt đầu thực thi operation (ISO 8601, UTC).
    /// Set tự động bởi <see cref="SetDuration"/> khi dùng <see cref="TimedAsync{T}"/>.
    /// </summary>
    [JsonPropertyName("started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartedAt { get; private set; }

    /// <summary>
    /// Thời điểm kết thúc thực thi operation (ISO 8601, UTC).
    /// Set tự động bởi <see cref="SetDuration"/> khi dùng <see cref="TimedAsync{T}"/>.
    /// </summary>
    [JsonPropertyName("end_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EndAt { get; private set; }

    /// <summary>
    /// Thời gian thực thi operation tính bằng milliseconds.
    /// Set tự động bởi <see cref="SetDuration"/> khi dùng <see cref="TimedAsync{T}"/>.
    /// </summary>
    [JsonPropertyName("duration_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; private set; }

    /// <summary>
    /// Kết quả thực thi operation: <c>success</c> hoặc <c>error</c>.
    /// Set tự động bởi <see cref="TimedAsync{T}"/> dựa trên kết quả exception hay không.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; private set; }

    /// <summary>
    /// Response thô từ TigerTMS sau khi gọi API.
    /// Được cắt tối đa 500 ký tự để tránh ES document quá lớn.
    /// Set qua <see cref="SetTigerResponse"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("tiger_response")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TigerRawResponse { get; private set; }

    /// <summary>
    /// Tên action được gọi sang TigerTMS, ví dụ: <c>CreateKey</c>, <c>CancelKey</c>.
    /// Dùng để phân biệt các operation khác nhau trong cùng một event.
    /// Set qua <see cref="SetAction"/> — bỏ qua nếu null/empty.
    /// </summary>
    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; private set; }

    /// <summary>
    /// Full type name của exception, ví dụ: <c>System.Net.Http.HttpRequestException</c>.
    /// Chỉ có mặt khi log level là ERROR. Set tự động bởi <see cref="ErrorAsync"/>.
    /// </summary>
    [JsonPropertyName("exception_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExceptionType { get; private set; }

    /// <summary>
    /// Message của exception — mô tả ngắn gọn lỗi xảy ra.
    /// Chỉ có mặt khi log level là ERROR. Set tự động bởi <see cref="ErrorAsync"/>.
    /// </summary>
    [JsonPropertyName("exception_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExceptionMessage { get; private set; }

    /// <summary>
    /// Stack trace đầy đủ của exception — dùng để debug xác định vị trí lỗi trong code.
    /// Chỉ có mặt khi log level là ERROR. Set tự động bởi <see cref="ErrorAsync"/>.
    /// </summary>
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

    /// <summary>
    /// Ghi log với level <c>INFO</c> — dành cho các sự kiện bình thường trong flow xử lý.
    /// </summary>
    public Task InfoAsync(string message, CancellationToken ct = default)
    {
        Timestamp = DateTimeOffset.UtcNow.ToString("o");
        Level     = "INFO";
        Message   = message;
        return _elastic.PostAsync(this, ct);
    }

    /// <summary>
    /// Ghi log với level <c>WARN</c> — dành cho các tình huống bất thường nhưng chưa phải lỗi nghiêm trọng
    /// (ví dụ: retry, fallback, dữ liệu không đầy đủ nhưng vẫn xử lý được).
    /// </summary>
    public Task WarnAsync(string message, CancellationToken ct = default)
    {
        Timestamp = DateTimeOffset.UtcNow.ToString("o");
        Level     = "WARN";
        Message   = message;
        return _elastic.PostAsync(this, ct);
    }

    /// <summary>
    /// Ghi log với level <c>ERROR</c> — dành cho các lỗi làm gián đoạn xử lý.
    /// Nếu truyền <paramref name="ex"/>, tự động điền <see cref="ExceptionType"/>,
    /// <see cref="ExceptionMessage"/>, <see cref="ExceptionStack"/>.
    /// </summary>
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