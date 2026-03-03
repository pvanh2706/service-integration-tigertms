namespace ServiceIntegration.Core.Abstractions;

/// <summary>
/// Định nghĩa các action được ghi vào trường <c>action</c> trong Elasticsearch log.
/// Dùng cùng <see cref="ElasticLogEntry.SetAction(string?)"/> qua extension <see cref="LogActionExtensions.ToValue"/>.
/// <para>Cách dùng:</para>
/// <code>
/// await log.SetAction(LogAction.CheckInReceived.ToValue()).InfoAsync("Nhận request từ client");
/// </code>
/// </summary>
public enum LogAction
{
    // ── HTTP Endpoint ──────────────────────────────────────────────────────

    /// <summary>Endpoint nhận request check-in từ client PMS.</summary>
    CheckInReceived,

    /// <summary>Endpoint đẩy event check-in vào RabbitMQ queue thành công.</summary>
    CheckInQueued,

    /// <summary>Endpoint thất bại khi publish event check-in lên queue.</summary>
    CheckInQueueFailed,

    // ── Worker / Event Handler ─────────────────────────────────────────────

    /// <summary>Worker bắt đầu xử lý event check-in lấy ra từ queue.</summary>
    CheckInProcessStart,

    /// <summary>Event check-in bị phát hiện trùng lặp (idempotency check) → bỏ qua.</summary>
    CheckInDuplicate,

    /// <summary>Payload của event check-in không hợp lệ → chuyển sang dead-letter queue.</summary>
    CheckInPayloadInvalid,

    /// <summary>Thiếu <c>wsuserkey</c> cần thiết để gọi TigerTMS → đẩy vào retry queue.</summary>
    CheckInMissingWsUserKey,

    /// <summary>Gọi API TigerTMS để tạo key phòng (CreateKey).</summary>
    CheckInTigerCall,

    /// <summary>TigerTMS phản hồi lỗi (business error) → đẩy vào retry queue.</summary>
    CheckInTigerFailed,

    /// <summary>Gọi callback về PMS để báo kết quả check-in.</summary>
    CheckInPmsCallback,

    /// <summary>Callback PMS thất bại → đẩy vào retry queue.</summary>
    CheckInPmsCallbackFailed,

    /// <summary>Xử lý event check-in hoàn tất thành công → ACK message.</summary>
    CheckInSuccess,

    /// <summary>Exception không xử lý được trong quá trình xử lý event → chuyển sang dead-letter queue.</summary>
    CheckInUnhandledException,
}

/// <summary>
/// Extension method để chuyển <see cref="LogAction"/> sang chuỗi ghi vào Elasticsearch.
/// </summary>
public static class LogActionExtensions
{
    /// <summary>
    /// Chuyển enum value sang chuỗi UPPER_SNAKE_CASE dùng cho trường <c>action</c> trong ES.
    /// </summary>
    public static string ToValue(this LogAction action) => action switch
    {
        // Endpoint
        LogAction.CheckInReceived          => "CHECKIN_RECEIVED",
        LogAction.CheckInQueued            => "CHECKIN_QUEUED",
        LogAction.CheckInQueueFailed       => "CHECKIN_QUEUE_FAILED",

        // Handler
        LogAction.CheckInProcessStart      => "CHECKIN_PROCESS_START",
        LogAction.CheckInDuplicate         => "CHECKIN_DUPLICATE",
        LogAction.CheckInPayloadInvalid    => "CHECKIN_PAYLOAD_INVALID",
        LogAction.CheckInMissingWsUserKey  => "CHECKIN_MISSING_WSUSER_KEY",
        LogAction.CheckInTigerCall         => "CHECKIN_TIGER_CALL",
        LogAction.CheckInTigerFailed       => "CHECKIN_TIGER_FAILED",
        LogAction.CheckInPmsCallback       => "CHECKIN_PMS_CALLBACK",
        LogAction.CheckInPmsCallbackFailed => "CHECKIN_PMS_CALLBACK_FAILED",
        LogAction.CheckInSuccess           => "CHECKIN_SUCCESS",
        LogAction.CheckInUnhandledException => "CHECKIN_UNHANDLED_EXCEPTION",

        _ => action.ToString().ToUpperInvariant()
    };
}
