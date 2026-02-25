namespace ServiceIntegrationDemo.Core.Abstractions;

public interface IQueueConsumer : IAsyncDisposable
{
    Task StartAsync(Func<ConsumedMessage, Task> onMessage, CancellationToken ct);
}

public sealed record ConsumedMessage(
    ReadOnlyMemory<byte> Body,
    MessageHeaders Headers,
    Func<Task> Ack,
    Func<bool, Task> Nack
);
