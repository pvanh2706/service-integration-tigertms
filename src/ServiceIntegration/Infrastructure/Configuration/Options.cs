namespace ServiceIntegration.Infrastructure.Configuration;

public sealed class RabbitOptions
{
    public string[] Nodes { get; set; } = Array.Empty<string>();
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = default!;
    public string Password { get; set; } = default!;

    public ExchangeOptions Exchanges { get; set; } = new();
    public QueueOptions Queues { get; set; } = new();
    public RoutingKeyOptions RoutingKeys { get; set; } = new();

    public sealed class ExchangeOptions
    {
        public string Events { get; set; } = "tigertms.events.x";
        public string Retry { get; set; } = "tigertms.retry.x";
    }

    public sealed class QueueOptions
    {
        public string Events { get; set; } = "tigertms.events.q";
        public string Dead { get; set; } = "tigertms.dead.q";
    }

    public sealed class RoutingKeyOptions
    {
        public string Events { get; set; } = "events";
        public string Retry10s { get; set; } = "retry.10s";
        public string Retry1m { get; set; } = "retry.1m";
        public string Retry5m { get; set; } = "retry.5m";
        public string Retry30m { get; set; } = "retry.30m";
        public string Dead { get; set; } = "dead";
    }
}

public sealed class ElasticOptions
{
    public bool Enabled { get; set; } = false;
    public string Uri { get; set; } = "http://localhost:9200";
    public string IndexPrefix { get; set; } = "ServiceIntegration";
}

public sealed class TigerOptions
{
    public bool Enabled { get; set; } = false;
    public string Endpoint { get; set; } = default!;
    public int TimeoutSeconds { get; set; } = 20;
    public string WsUserKey { get; set; } = default!;
    public string SoapAction { get; set; } = "";
}

public sealed class PmsCallbackOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:5080";
    public int TimeoutSeconds { get; set; } = 10;
}
