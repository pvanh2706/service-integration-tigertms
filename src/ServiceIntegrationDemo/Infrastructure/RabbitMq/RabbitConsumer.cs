using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServiceIntegrationDemo.Core.Abstractions;
using System.Text;

namespace ServiceIntegrationDemo.Infrastructure.RabbitMq;

public sealed class RabbitConsumer : IQueueConsumer
{
    private readonly RabbitOptions _opt;
    private readonly RabbitConnectionFactory _factory;
    private readonly ILogger<RabbitConsumer> _logger;

    private IConnection? _conn;
    private IModel? _ch;

    public RabbitConsumer(IOptions<RabbitOptions> opt, RabbitConnectionFactory factory, ILogger<RabbitConsumer> logger)
    {
        _opt = opt.Value;
        _factory = factory;
        _logger = logger;
    }

    public Task StartAsync(Func<ConsumedMessage, Task> onMessage, CancellationToken ct)
    {
        _conn = _factory.Create();
        _ch = _conn.CreateModel();

        _ch.BasicQos(0, prefetchCount: 20, global: false);

        var consumer = new AsyncEventingBasicConsumer(_ch);
        consumer.Received += async (_, ea) =>
        {
            var headers = new MessageHeaders();
            if (ea.BasicProperties?.Headers is not null)
            {
                foreach (var kv in ea.BasicProperties.Headers)
                {
                    headers.Set(kv.Key, kv.Value is byte[] b ? Encoding.UTF8.GetString(b) : kv.Value);
                }
            }

            Task Ack()
            {
                _ch.BasicAck(ea.DeliveryTag, multiple: false);
                return Task.CompletedTask;
            }

            Task Nack(bool requeue)
            {
                _ch.BasicNack(ea.DeliveryTag, multiple: false, requeue: requeue);
                return Task.CompletedTask;
            }

            await onMessage(new ConsumedMessage(ea.Body, headers, Ack, Nack));
        };

        _ch.BasicConsume(_opt.Queues.Events, autoAck: false, consumer: consumer);
        _logger.LogInformation("Rabbit consumer started. queue={Queue}", _opt.Queues.Events);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try { _ch?.Close(); } catch { }
        try { _conn?.Close(); } catch { }
        return ValueTask.CompletedTask;
    }
}
