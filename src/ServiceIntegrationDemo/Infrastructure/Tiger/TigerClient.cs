using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceIntegrationDemo.Core.Abstractions;
using ServiceIntegrationDemo.Infrastructure;

namespace ServiceIntegrationDemo.Infrastructure.Tiger;

public sealed class TigerClient : ITigerClient
{
    private readonly TigerOptions _opt;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TigerClient> _logger;

    public TigerClient(IOptions<TigerOptions> opt, IHttpClientFactory httpFactory, ILogger<TigerClient> logger)
    {
        _opt = opt.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<TigerResult> SendCheckInAsync(string innerXml, CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            _logger.LogInformation("TigerTms.Enabled=false -> mock SUCCESS");
            await Task.Delay(80, ct);
            return new TigerResult(true, "SUCCESS");
        }

        var escaped = TigerSoapBuilder.EscapeInnerXml(innerXml);
        var soap = TigerSoapBuilder.WrapCheckIn(escaped);

        var client = _httpFactory.CreateClient("TigerTms");
        using var req = new HttpRequestMessage(HttpMethod.Post, _opt.Endpoint);
        req.Content = new StringContent(soap, System.Text.Encoding.UTF8, "text/xml");
        if (!string.IsNullOrWhiteSpace(_opt.SoapAction))
            req.Headers.Add("SOAPAction", _opt.SoapAction);

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        var isSuccess = raw.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
        if (!isSuccess)
        {
            var reason = raw.Length > 300 ? raw[..300] : raw;
            return new TigerResult(false, raw, reason);
        }

        return new TigerResult(true, raw);
    }
}
