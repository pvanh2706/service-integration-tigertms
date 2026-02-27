using ServiceIntegration.Core.Abstractions;

namespace ServiceIntegration.Core.Services;

public sealed class EventHandlerRegistry
{
    private readonly Dictionary<string, IEventHandler> _handlers;

    public EventHandlerRegistry(IEnumerable<IEventHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.EventType, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string eventType, out IEventHandler? handler)
        => _handlers.TryGetValue(eventType, out handler);
}
