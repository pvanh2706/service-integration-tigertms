using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ServiceIntegrationDemo.Infrastructure.RabbitMq;

public sealed class RabbitTopology
{
    private readonly RabbitOptions _opt;
    private readonly RabbitConnectionFactory _factory;
    private readonly ILogger<RabbitTopology> _logger;

    public RabbitTopology(IOptions<RabbitOptions> opt, RabbitConnectionFactory factory, ILogger<RabbitTopology> logger)
    {
        _opt = opt.Value;
        _factory = factory;
        _logger = logger;
    }

    public void Ensure()
    {
        using var conn = _factory.Create();
        using var ch = conn.CreateModel();

        ch.ExchangeDeclare(_opt.Exchanges.Events, ExchangeType.Direct, durable: true, autoDelete: false);
        ch.ExchangeDeclare(_opt.Exchanges.Retry, ExchangeType.Direct, durable: true, autoDelete: false);

        ch.QueueDeclare(_opt.Queues.Events, durable: true, exclusive: false, autoDelete: false);
        ch.QueueBind(_opt.Queues.Events, _opt.Exchanges.Events, _opt.RoutingKeys.Events);

        ch.QueueDeclare(_opt.Queues.Dead, durable: true, exclusive: false, autoDelete: false);
        ch.QueueBind(_opt.Queues.Dead, _opt.Exchanges.Retry, _opt.RoutingKeys.Dead);

        _logger.LogInformation("Rabbit topology ensured (exchanges/queues/bindings).");
    }
}
