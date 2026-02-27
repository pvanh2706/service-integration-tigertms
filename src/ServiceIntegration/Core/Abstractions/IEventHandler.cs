namespace ServiceIntegration.Core.Abstractions;

public interface IEventHandler
{
    string EventType { get; }
    Task HandleAsync(EventContext ctx, CancellationToken ct);
}

public sealed record EventContext(
    string HotelId,
    string EventId,
    string CorrelationId,
    MessageHeaders Headers,
    ReadOnlyMemory<byte> Body,
    Func<Task> Ack,
    Func<bool, Task> Nack
);
