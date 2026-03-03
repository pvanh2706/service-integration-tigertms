using Microsoft.Extensions.Options;

namespace ServiceIntegration.Core.Services;

public sealed class RetryRouter
{
    private readonly RetryPolicyOptions _opt;

    public RetryRouter(IOptions<RetryPolicyOptions> opt)
    {
        _opt = opt.Value;
    }

    /// <summary>
    /// attempt = số lần đã thử (0-based).
    /// Nếu đã đủ MaxAttempts thì chuyển Dead, ngược lại retry với interval cố định.
    /// </summary>
    public RetryDecision Decide(int attempt)
    {
        if (attempt >= _opt.MaxAttempts - 1)
            return new RetryDecision(RetryRoute.Dead);

        return new RetryDecision(RetryRoute.Retry);
    }

    public int MaxAttempts => _opt.MaxAttempts;
}

public enum RetryRoute { Retry, Dead }

public sealed record RetryDecision(RetryRoute Route);

public sealed class RetryPolicyOptions
{
    /// <summary>Số lần retry tối đa trước khi chuyển Dead.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Khoảng thời gian giữa mỗi lần retry (giây).</summary>
    public int IntervalSeconds { get; set; } = 20;
}
