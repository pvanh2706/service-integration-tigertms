// using Microsoft.Extensions.Options;
// using RabbitMQ.Client;
// using ServiceIntegrationDemo.Core.Abstractions;
// using ServiceIntegrationDemo.Core.Services;

// namespace ServiceIntegrationDemo.Infrastructure.RabbitMq;

// public sealed class RabbitPublisher : IIntegrationQueue
// {
//     private readonly RabbitOptions _opt;
//     private readonly RabbitConnectionFactory _factory;

//     public RabbitPublisher(IOptions<RabbitOptions> opt, RabbitConnectionFactory factory)
//     {
//         _opt = opt.Value;
//         _factory = factory;
//     }

//     public Task PublishAsync(ReadOnlyMemory<byte> body, MessageHeaders headers, CancellationToken ct)
//     {
//         using var conn = _factory.Create();
//         using var ch = conn.CreateModel();

//         var props = ch.CreateBasicProperties();
//         props.Persistent = true;
//         props.Headers = headers.AsReadOnly().ToDictionary(kv => kv.Key, kv => kv.Value);

//         ch.BasicPublish(_opt.Exchanges.Events, _opt.RoutingKeys.Events, props, body);
//         return Task.CompletedTask;
//     }

//     public Task PublishToRetryAsync(ReadOnlyMemory<byte> body, IDictionary<string, object> headers, string routingKey)
//     {
//         using var conn = _factory.Create();
//         using var ch = conn.CreateModel();

//         var props = ch.CreateBasicProperties();
//         props.Persistent = true;
//         props.Headers = headers;

//         ch.BasicPublish(_opt.Exchanges.Retry, routingKey, props, body);
//         return Task.CompletedTask;
//     }

//     public string RoutingKeyForRetry(RetryRoute route) => route switch
//     {
//         RetryRoute.Retry10s => _opt.RoutingKeys.Retry10s,
//         RetryRoute.Retry1m => _opt.RoutingKeys.Retry1m,
//         RetryRoute.Retry5m => _opt.RoutingKeys.Retry5m,
//         RetryRoute.Retry30m => _opt.RoutingKeys.Retry30m,
//         _ => _opt.RoutingKeys.Dead
//     };
// }
