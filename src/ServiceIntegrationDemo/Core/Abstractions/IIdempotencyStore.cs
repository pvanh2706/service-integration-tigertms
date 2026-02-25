namespace ServiceIntegrationDemo.Core.Abstractions;

public interface IIdempotencyStore
{
    bool SeenRecently(string hotelId, string eventId);
    void MarkSeen(string hotelId, string eventId, TimeSpan ttl);
}
