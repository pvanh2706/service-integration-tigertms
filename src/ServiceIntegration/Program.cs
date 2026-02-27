using Serilog;
using ServiceIntegration.Endpoints;
using ServiceIntegration.Extensions;
using ServiceIntegration.Infrastructure.RabbitMq;

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
builder.Services.AddMemoryCache();

// Đăng ký Options từ appsettings.
builder.Services.AddAppOptions(builder.Configuration);
// Đăng ký HttpClient cho từng external service.
builder.Services.AddAppHttpClients();
// Đăng ký các thành phần Infrastructure (RabbitMQ, Tiger, PMS, Elastic, Idempotency).
builder.Services.AddAppInfrastructure();
// Đăng ký các service chính của ứng dụng (IntegrationService, Workers).
builder.Services.AddAppServices();

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

// Map endpoints trước để có thể truy cập ngay cả khi RabbitMQ chưa sẵn sàng (không phụ thuộc vào RabbitMQ)
app.MapPmsEndpoints();
app.MapCheckInEndpoints();

app.Run();

