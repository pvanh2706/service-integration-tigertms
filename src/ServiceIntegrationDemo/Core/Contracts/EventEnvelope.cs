using System.Text.Json;

namespace ServiceIntegrationDemo.Core.Contracts;

public sealed class EventEnvelope
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string HotelId { get; set; } = default!;
    public string EventType { get; set; } = default!; // e.g. CHECKIN
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public JsonElement Payload { get; set; }
}
