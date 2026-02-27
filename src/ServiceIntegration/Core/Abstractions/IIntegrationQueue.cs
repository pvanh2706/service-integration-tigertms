namespace ServiceIntegration.Core.Abstractions;

public interface IIntegrationQueue
{
    Task PublishAsync(ReadOnlyMemory<byte> body, MessageHeaders headers, CancellationToken ct);
}
