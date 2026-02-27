using Microsoft.Extensions.Options;
using Serilog;
using ServiceIntegration.Core.Abstractions;
using ServiceIntegration.Core.Services;
using ServiceIntegration.Infrastructure.Configuration;
using ServiceIntegration.Infrastructure.Elastic;
using ServiceIntegration.Infrastructure.Idempotency;
using ServiceIntegration.Infrastructure.Pms;
using ServiceIntegration.Infrastructure.RabbitMq;
using ServiceIntegration.Infrastructure.TigerTms;
using ServiceIntegration.Infrastructure.Workers;
using ServiceIntegration.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Windows Service ready:
// builder.Host.UseWindowsService();

// Serilog: chỉ ghi ra Console.
// Log lên Elasticsearch được thực hiện chủ động qua IElasticLogger (không tự động).
builder.Host.UseSerilog((ctx, lc) =>
{
    lc.MinimumLevel.Information()
      .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
      .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
      .Enrich.FromLogContext()
      .WriteTo.Console();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RabbitOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<TigerOptions>(builder.Configuration.GetSection("TigerTms"));
builder.Services.Configure<PmsCallbackOptions>(builder.Configuration.GetSection("PmsCallback"));
builder.Services.Configure<RetryPolicyOptions>(builder.Configuration.GetSection("RetryPolicy"));

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("TigerTms", (sp, client) =>
{
    var opt = sp.GetRequiredService<IOptions<TigerOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(3, opt.TimeoutSeconds));
});

builder.Services.AddHttpClient("PmsCallback", (sp, client) =>
{
    var opt = sp.GetRequiredService<IOptions<PmsCallbackOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(3, opt.TimeoutSeconds));
});

// HttpClient riêng cho ElasticLogger (không bị cắt timeout ngắn như TigerTms)
builder.Services.AddHttpClient("Elastic", (sp, client) =>
{
    var opt = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
    client.BaseAddress = Uri.TryCreate(opt.Uri, UriKind.Absolute, out var uri) ? uri : null;
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddSingleton<RabbitConnectionFactory>();
builder.Services.AddSingleton<RabbitTopology>();
builder.Services.AddSingleton<RabbitPublisher>();
builder.Services.AddSingleton<IIntegrationQueue>(sp => sp.GetRequiredService<RabbitPublisher>());
builder.Services.AddSingleton<IQueueConsumer, RabbitConsumer>();

builder.Services.AddSingleton<IElasticLogger, ElasticLogger>();

builder.Services.AddSingleton<ITigerClient, TigerClient>();
builder.Services.AddSingleton<IPmsCallbackClient, PmsCallbackClient>();
builder.Services.AddSingleton<IIdempotencyStore, MemoryIdempotencyStore>();

builder.Services.AddSingleton<RetryRouter>();
builder.Services.AddSingleton<IEventHandler, CheckInEventHandler>();
builder.Services.AddSingleton<EventHandlerRegistry>();
builder.Services.AddSingleton<MessageOrchestrator>();

builder.Services.AddHostedService<QueueWorker>();

var app = builder.Build();

// app.Services.GetRequiredService<RabbitTopology>().Ensure();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
try
{
    app.Services.GetRequiredService<RabbitTopology>().Ensure();
}
catch (Exception ex)
{
    logger.LogError(ex, "Không kết nối được RabbitMQ. Service vẫn chạy nhưng chưa publish/consume được.");
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapPmsEndpoints();
app.MapCheckInEndpoints();

app.Run();

