using Microsoft.Extensions.Options;

namespace ServiceIntegration.Core.Services;

public sealed class RetryRouter
{
    private readonly RetryPolicyOptions _opt;

    public RetryRouter(IOptions<RetryPolicyOptions> opt)
    {
        _opt = opt.Value;
    }

    public RetryDecision Decide(int attempt)
    {
        if (attempt <= 0) return new RetryDecision(RetryRoute.Retry10s);
        if (attempt == 1) return new RetryDecision(RetryRoute.Retry1m);
        if (attempt == 2) return new RetryDecision(RetryRoute.Retry5m);
        if (attempt == 3) return new RetryDecision(RetryRoute.Retry30m);
        return new RetryDecision(RetryRoute.Dead);
    }

    public int MaxAttempts => _opt.MaxAttempts;
}

public enum RetryRoute { Retry10s, Retry1m, Retry5m, Retry30m, Dead }

public sealed record RetryDecision(RetryRoute Route);

public sealed class RetryPolicyOptions
{
    public int MaxAttempts { get; set; } = 5;
}
