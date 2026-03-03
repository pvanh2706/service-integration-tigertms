using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ServiceIntegration.Core.Services;
using ServiceIntegration.Infrastructure.Configuration;

namespace ServiceIntegration.Infrastructure.RabbitMq;

public sealed class RabbitTopology
{
    private readonly RabbitOptions _opt;
    private readonly RetryPolicyOptions _retryOpt;
    private readonly RabbitConnectionFactory _factory;
    private readonly ILogger<RabbitTopology> _logger;

    public RabbitTopology(
        IOptions<RabbitOptions> opt,
        IOptions<RetryPolicyOptions> retryOpt,
        RabbitConnectionFactory factory,
        ILogger<RabbitTopology> logger)
    {
        _opt = opt.Value;
        _retryOpt = retryOpt.Value;
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

        // Retry queue duy nhất: TTL cố định -> dead-letter về events exchange để xử lý lại
        var retryTtlMs = _retryOpt.IntervalSeconds * 1_000;
        var retryArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"]             = retryTtlMs,
            ["x-dead-letter-exchange"]    = _opt.Exchanges.Events,
            ["x-dead-letter-routing-key"] = _opt.RoutingKeys.Events,
        };
        ch.QueueDeclare(_opt.Queues.Retry, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);
        ch.QueueBind(_opt.Queues.Retry, _opt.Exchanges.Retry, _opt.RoutingKeys.Retry);
        _logger.LogInformation("Retry queue declared: {Queue} (ttl={Ttl}ms, maxAttempts={Max}) bound to {Exchange}/{RoutingKey}",
            _opt.Queues.Retry, retryTtlMs, _retryOpt.MaxAttempts, _opt.Exchanges.Retry, _opt.RoutingKeys.Retry);

        _logger.LogInformation("Rabbit topology ensured (exchanges/queues/bindings).");
    }
}
