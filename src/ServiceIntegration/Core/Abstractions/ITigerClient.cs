namespace ServiceIntegration.Core.Abstractions;

public interface ITigerClient
{
    Task<TigerResult> SendCheckInAsync(string innerXml, CancellationToken ct);
}

public sealed record TigerResult(bool IsSuccess, string RawResponse, string? FailureReason = null);
