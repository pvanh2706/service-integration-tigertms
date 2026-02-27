using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceIntegration.Core.Abstractions;
using ServiceIntegration.Core.Services;

namespace ServiceIntegration.Infrastructure.Workers;

public sealed class QueueWorker : BackgroundService
{
    private readonly ILogger<QueueWorker> _logger;
    private readonly IQueueConsumer _consumer;
    private readonly MessageOrchestrator _orchestrator;

    public QueueWorker(ILogger<QueueWorker> logger, IQueueConsumer consumer, MessageOrchestrator orchestrator)
    {
        _logger = logger;
        _consumer = consumer;
        _orchestrator = orchestrator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QueueWorker starting...");

        await _consumer.StartAsync(async msg =>
        {
            await _orchestrator.ProcessAsync(msg, stoppingToken);
        }, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QueueWorker stopping...");
        await _consumer.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
