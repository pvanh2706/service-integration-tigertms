using Microsoft.Extensions.Caching.Memory;
using ServiceIntegrationDemo.Core.Abstractions;

namespace ServiceIntegrationDemo.Infrastructure;

public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly IMemoryCache _cache;

    public MemoryIdempotencyStore(IMemoryCache cache) => _cache = cache;

    public bool SeenRecently(string hotelId, string eventId)
        => _cache.TryGetValue(Key(hotelId, eventId), out _);

    public void MarkSeen(string hotelId, string eventId, TimeSpan ttl)
        => _cache.Set(Key(hotelId, eventId), true, ttl);

    private static string Key(string hotelId, string eventId) => $"seen:{hotelId}:{eventId}";
}
