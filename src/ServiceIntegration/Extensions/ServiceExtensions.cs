using Microsoft.Extensions.Options;
using ServiceIntegration.Core.Abstractions;
using ServiceIntegration.Core.Services;
using ServiceIntegration.Infrastructure.Configuration;
using ServiceIntegration.Infrastructure.Elastic;
using ServiceIntegration.Infrastructure.Idempotency;
using ServiceIntegration.Infrastructure.Pms;
using ServiceIntegration.Infrastructure.RabbitMq;
using ServiceIntegration.Infrastructure.TigerTms;
using ServiceIntegration.Infrastructure.Workers;

namespace ServiceIntegration.Extensions;

public static class ServiceExtensions
{
    /// <summary>Đăng ký Options từ appsettings.</summary>
    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration config)
    {
        // Láy config từ appsettings và bind vào Options, sau đó inject IOptions<T> cho các class cần.
        services.Configure<ElasticOptions>(config.GetSection("Elastic"));
        services.Configure<RabbitOptions>(config.GetSection("RabbitMq"));
        services.Configure<TigerOptions>(config.GetSection("TigerTms"));
        services.Configure<PmsCallbackOptions>(config.GetSection("PmsCallback"));
        services.Configure<RetryPolicyOptions>(config.GetSection("RetryPolicy"));
        return services;
    }

    /// <summary>Đăng ký HttpClient cho từng external service.</summary>
    public static IServiceCollection AddAppHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient("TigerTms", (sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<TigerOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(3, opt.TimeoutSeconds));
        });

        services.AddHttpClient("PmsCallback", (sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<PmsCallbackOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(3, opt.TimeoutSeconds));
        });

        // HttpClient riêng cho ElasticLogger (không bị cắt timeout ngắn như TigerTms)
        services.AddHttpClient("Elastic", (sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
            client.BaseAddress = Uri.TryCreate(opt.Uri, UriKind.Absolute, out var uri) ? uri : null;
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }

    /// <summary>Đăng ký các thành phần Infrastructure (RabbitMQ, Tiger, PMS, Elastic, Idempotency).</summary>
    public static IServiceCollection AddAppInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<RabbitConnectionFactory>();
        services.AddSingleton<RabbitTopology>();
        services.AddSingleton<RabbitPublisher>();
        services.AddSingleton<IIntegrationQueue>(sp => sp.GetRequiredService<RabbitPublisher>());
        services.AddSingleton<IQueueConsumer, RabbitConsumer>();

        services.AddSingleton<IElasticLogger, ElasticLogger>();
        services.AddSingleton<ITigerClient, TigerClient>();
        services.AddSingleton<IPmsCallbackClient, PmsCallbackClient>();
        services.AddSingleton<IIdempotencyStore, MemoryIdempotencyStore>();

        return services;
    }

    /// <summary>Đăng ký các Services, Handlers và Workers.</summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<RetryRouter>();
        services.AddSingleton<IEventHandler, CheckInEventHandler>();
        services.AddSingleton<EventHandlerRegistry>();
        services.AddSingleton<MessageOrchestrator>();

        services.AddHostedService<QueueWorker>();

        return services;
    }
}
