using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Sinks.Elasticsearch;
using ServiceIntegrationDemo.Core.Abstractions;
using ServiceIntegrationDemo.Core.Contracts;
using ServiceIntegrationDemo.Core.Services;
using ServiceIntegrationDemo.Infrastructure;
using ServiceIntegrationDemo.Infrastructure.Pms;
using ServiceIntegrationDemo.Infrastructure.RabbitMq;
using ServiceIntegrationDemo.Infrastructure.Tiger;
using ServiceIntegrationDemo.Infrastructure.Worker;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Windows Service ready:
// builder.Host.UseWindowsService();

builder.Host.UseSerilog((ctx, lc) =>
{
    var elastic = ctx.Configuration.GetSection("Elastic").Get<ElasticOptions>() ?? new ElasticOptions();
    // lc.Enrich.FromLogContext()
    // Bỏ MinimumLevel.Verbose() để giảm log chi tiết, chỉ còn Information trở lên
    lc.MinimumLevel.Information()
      .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
      .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
      .Enrich.FromLogContext()
      .WriteTo.Console();
    // Kết thúc bỏ MinimumLevel.Verbose() để giảm log chi tiết, chỉ còn Information trở lên

    if (elastic.Enabled && Uri.TryCreate(elastic.Uri, UriKind.Absolute, out var uri))
    {
        lc.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(uri)
        {
            AutoRegisterTemplate = true,
            IndexFormat = $"{elastic.IndexPrefix}"
        });
    }
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

builder.Services.AddSingleton<RabbitConnectionFactory>();
builder.Services.AddSingleton<RabbitTopology>();
builder.Services.AddSingleton<RabbitPublisher>();
builder.Services.AddSingleton<IIntegrationQueue>(sp => sp.GetRequiredService<RabbitPublisher>());
builder.Services.AddSingleton<IQueueConsumer, RabbitConsumer>();

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

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// Demo endpoint: PMS callback
app.MapPost("/pms/callback", ([FromBody] PmsCallbackRequest req) =>
{
    return Results.Ok(new { ok = true, req.EventId, req.TigerStatus });
});

// Receive CHECKIN event (PMS -> Integration)
app.MapPost("/events/checkin", async (
    [FromBody] EventEnvelope envelope,
    IIntegrationQueue queue,
    IOptions<TigerOptions> tigerOpt) =>
{
    if (string.IsNullOrWhiteSpace(envelope.HotelId)) return Results.BadRequest("hotelId is required");
    if (string.IsNullOrWhiteSpace(envelope.EventId)) return Results.BadRequest("eventId is required");

    envelope.EventType = "CHECKIN";

    // Enqueue only payload bytes (handlers parse payload)
    var payloadJson = JsonSerializer.Serialize(envelope.Payload);
    var body = Encoding.UTF8.GetBytes(payloadJson);

    var headers = new MessageHeaders();
    headers.Set("x-hotel-id", envelope.HotelId);
    headers.Set("x-event-id", envelope.EventId);
    headers.Set("x-event-type", envelope.EventType);
    headers.Set("x-correlation-id", Guid.NewGuid().ToString("N"));
    headers.Set("x-attempt", 0);

    // Demo: use global wsuserkey from config; production: load by HotelId (DB/config service)
    var wsuserkey = tigerOpt.Value.WsUserKey;
    if (!string.IsNullOrWhiteSpace(wsuserkey))
        headers.Set("x-wsuserkey", wsuserkey);

    await queue.PublishAsync(body, headers, CancellationToken.None);
    return Results.Ok(new { status = "QUEUED", envelope.EventId, envelope.HotelId });
})
.WithName("CheckIn");

app.Run();

