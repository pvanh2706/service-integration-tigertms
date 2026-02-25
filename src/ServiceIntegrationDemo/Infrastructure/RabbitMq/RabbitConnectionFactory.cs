using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ServiceIntegrationDemo.Infrastructure.RabbitMq;

public sealed class RabbitConnectionFactory
{
    private readonly RabbitOptions _opt;

    public RabbitConnectionFactory(IOptions<RabbitOptions> opt)
    {
        _opt = opt.Value;
    }

    public IConnection Create()
    {
        var factory = new ConnectionFactory
        {
            UserName = _opt.UserName,
            Password = _opt.Password,
            VirtualHost = _opt.VirtualHost,
            Port = _opt.Port,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        var endpoints = _opt.Nodes.Select(n => new AmqpTcpEndpoint(n, _opt.Port)).ToList();
        return factory.CreateConnection(endpoints);
    }
}
