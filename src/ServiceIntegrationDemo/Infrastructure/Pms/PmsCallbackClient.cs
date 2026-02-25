using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceIntegrationDemo.Core.Abstractions;
using ServiceIntegrationDemo.Infrastructure;

namespace ServiceIntegrationDemo.Infrastructure.Pms;

public sealed class PmsCallbackClient : IPmsCallbackClient
{
    private readonly PmsCallbackOptions _opt;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PmsCallbackClient> _logger;

    public PmsCallbackClient(IOptions<PmsCallbackOptions> opt, IHttpClientFactory httpFactory, ILogger<PmsCallbackClient> logger)
    {
        _opt = opt.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<bool> NotifyAsync(PmsCallbackRequest req, CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            _logger.LogInformation("PmsCallback.Enabled=false -> skip callback (treat OK)");
            return true;
        }

        var client = _httpFactory.CreateClient("PmsCallback");
        var url = new Uri(new Uri(_opt.BaseUrl), "/pms/callback").ToString();

        var resp = await client.PostAsJsonAsync(url, req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("PMS callback failed status={Status}", resp.StatusCode);
            return false;
        }
        return true;
    }
}
