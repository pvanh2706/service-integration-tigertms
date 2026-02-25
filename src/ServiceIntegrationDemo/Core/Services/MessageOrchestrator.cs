using Microsoft.Extensions.Logging;
using ServiceIntegrationDemo.Core.Abstractions;

namespace ServiceIntegrationDemo.Core.Services;

public sealed class MessageOrchestrator
{
    private readonly ILogger<MessageOrchestrator> _logger;
    private readonly EventHandlerRegistry _registry;

    public MessageOrchestrator(ILogger<MessageOrchestrator> logger, EventHandlerRegistry registry)
    {
        _logger = logger;
        _registry = registry;
    }

    public async Task ProcessAsync(ConsumedMessage msg, CancellationToken ct)
    {
        var hotelId = msg.Headers.GetString("x-hotel-id");
        var eventId = msg.Headers.GetString("x-event-id");
        var eventType = msg.Headers.GetString("x-event-type");
        var correlationId = msg.Headers.GetString("x-correlation-id");

        if (string.IsNullOrWhiteSpace(hotelId) || string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("Missing required headers -> ACK to avoid poison loop. hotelId={HotelId} eventId={EventId} eventType={EventType}",
                hotelId, eventId, eventType);
            await msg.Ack();
            return;
        }

        if (!_registry.TryGet(eventType, out var handler) || handler is null)
        {
            _logger.LogWarning("No handler for eventType={EventType} -> ACK (ignore unknown).", eventType);
            await msg.Ack();
            return;
        }

        var ctx = new EventContext(hotelId, eventId, correlationId, msg.Headers, msg.Body, msg.Ack, msg.Nack);
        await handler.HandleAsync(ctx, ct);
    }
}
