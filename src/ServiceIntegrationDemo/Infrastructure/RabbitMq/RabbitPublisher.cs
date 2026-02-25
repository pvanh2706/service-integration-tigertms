using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ServiceIntegrationDemo.Core.Abstractions;
using ServiceIntegrationDemo.Infrastructure;
using System.Collections.Concurrent;

namespace ServiceIntegrationDemo.Infrastructure.RabbitMq;

/// <summary>
/// Publisher chuẩn production:
/// - 1 connection dùng chung (expensive)
/// - channel tạo theo từng publish (cheap) để tránh thread-safety issue
/// - hỗ trợ publisher confirms (tuỳ chọn)
/// - tự reconnect nếu connection bị drop
/// </summary>
public sealed class RabbitPublisher : IIntegrationQueue, IDisposable
{
    private readonly RabbitOptions _opt;
    private readonly RabbitConnectionFactory _factory;
    private readonly ILogger<RabbitPublisher> _logger;

    private readonly object _connLock = new();
    private IConnection? _conn;

    public RabbitPublisher(
        IOptions<RabbitOptions> opt,
        RabbitConnectionFactory factory,
        ILogger<RabbitPublisher> logger)
    {
        _opt = opt.Value;
        _factory = factory;
        _logger = logger;
    }

    private IConnection GetOrCreateConnection()
    {
        // Fast path
        if (_conn is { IsOpen: true }) return _conn;

        lock (_connLock)
        {
            if (_conn is { IsOpen: true }) return _conn;

            try
            {
                _conn?.Dispose();
            }
            catch { /* ignore */ }

            _conn = _factory.Create();

            _conn.ConnectionShutdown += (_, ea) =>
            {
                _logger.LogWarning("RabbitMQ connection shutdown: replyCode={Code}, replyText={Text}",
                    ea.ReplyCode, ea.ReplyText);
            };

            _logger.LogInformation("RabbitMQ publisher connection created.");
            return _conn;
        }
    }

    public Task PublishAsync(ReadOnlyMemory<byte> body, MessageHeaders headers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var conn = GetOrCreateConnection();

        // Channel KHÔNG thread-safe => tạo channel theo từng publish
        using var ch = conn.CreateModel();

        // (Tùy chọn) Confirm mode để broker xác nhận đã nhận message
        // Bạn có thể bật mặc định luôn cho production
        ch.ConfirmSelect();

        var props = ch.CreateBasicProperties();
        props.Persistent = true; // message persistent (kèm queue durable)
        props.Headers = headers.AsReadOnly().ToDictionary(kv => kv.Key, kv => kv.Value);

        ch.BasicPublish(
            exchange: _opt.Exchanges.Events,
            routingKey: _opt.RoutingKeys.Events,
            basicProperties: props,
            body: body
        );

        // Chờ confirm (timeout ngắn). Nếu timeout/failed => throw để upstream xử lý (log/retry)
        var ok = ch.WaitForConfirms(TimeSpan.FromSeconds(5));
        if (!ok)
        {
            throw new Exception("RabbitMQ publish confirm failed/timeout.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publish vào exchange retry (dùng cho retry republish)
    /// </summary>
    public Task PublishToRetryAsync(ReadOnlyMemory<byte> body, IDictionary<string, object> headers, string routingKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = GetOrCreateConnection();
        using var ch = conn.CreateModel();
        ch.ConfirmSelect();

        var props = ch.CreateBasicProperties();
        props.Persistent = true;
        props.Headers = headers;

        ch.BasicPublish(
            exchange: _opt.Exchanges.Retry,
            routingKey: routingKey,
            basicProperties: props,
            body: body
        );

        var ok = ch.WaitForConfirms(TimeSpan.FromSeconds(5));
        if (!ok)
        {
            throw new Exception("RabbitMQ retry publish confirm failed/timeout.");
        }

        return Task.CompletedTask;
    }

    public string RoutingKeyForRetry(Core.Services.RetryRoute route) => route switch
    {
        Core.Services.RetryRoute.Retry10s => _opt.RoutingKeys.Retry10s,
        Core.Services.RetryRoute.Retry1m => _opt.RoutingKeys.Retry1m,
        Core.Services.RetryRoute.Retry5m => _opt.RoutingKeys.Retry5m,
        Core.Services.RetryRoute.Retry30m => _opt.RoutingKeys.Retry30m,
        _ => _opt.RoutingKeys.Dead
    };

    public void Dispose()
    {
        try { _conn?.Close(); } catch { }
        try { _conn?.Dispose(); } catch { }
    }
}