namespace ServiceIntegrationDemo.Core.Abstractions;

public interface IPmsCallbackClient
{
    Task<bool> NotifyAsync(PmsCallbackRequest req, CancellationToken ct);
}

public sealed record PmsCallbackRequest(
    string HotelId,
    string EventId,
    string EventType,
    string TigerStatus,
    string? TigerReason,
    string CorrelationId
);
