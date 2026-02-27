using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceIntegration.Core.Abstractions;
using ServiceIntegration.Infrastructure.Configuration;

namespace ServiceIntegration.Infrastructure.Elastic;

/// <summary>
/// Gửi <see cref="ElasticLogEntry"/> lên Elasticsearch qua HTTP.
/// Không phụ thuộc Serilog sink - hoàn toàn tường minh, caller kiểm soát điểm ghi.
/// </summary>
public sealed class ElasticLogger : IElasticLogger
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ElasticOptions _options;
    private readonly ILogger<ElasticLogger> _fallback;

    private static readonly JsonSerializerOptions _json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public ElasticLogger(
        IHttpClientFactory httpFactory,
        IOptions<ElasticOptions> options,
        ILogger<ElasticLogger> fallback)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _fallback    = fallback;
    }

    public async Task PostAsync(ElasticLogEntry entry, CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        try
        {
            var indexDate = DateTimeOffset.UtcNow.ToString("yyyy.MM.dd");
            var index     = $"{_options.IndexPrefix}-{indexDate}";
            var json      = JsonSerializer.Serialize(entry, _json);
            var content   = new StringContent(json, Encoding.UTF8, "application/json");

            var client   = _httpFactory.CreateClient("Elastic");
            var url      = $"{_options.Uri.TrimEnd('/')}/{index}/_doc/";
            var response = await client.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _fallback.LogWarning("ElasticLogger: POST thất bại [{Status}] {Body}",
                    (int)response.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _fallback.LogWarning(ex, "ElasticLogger: Không ghi được log lên Elasticsearch");
        }
    }
}