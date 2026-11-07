using System.Security.Cryptography;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Runs an operation again when it fails for a reason that is likely to resolve on its own.</summary>
internal static class RetryStrategy
{
    /// <summary>The number of times the operation is run, the first attempt included.</summary>
    public const int MaxAttempts = 3;

    /// <summary>The delay before the first retry. It doubles on every subsequent attempt.</summary>
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(1);

    private const int MaxJitterPercentage = 25;

    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, Func<Exception, bool> isTransient, Action<Exception, int, TimeSpan>? onRetry, TimeSpan baseDelay, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            // A cancellation asked for by the caller is never a transient failure, whatever the operation reports: the
            // token is the one thing the caller expects to stop the retries instead of being waited out.
            catch (Exception exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested && isTransient(exception))
            {
                var delay = GetDelay(attempt, baseDelay);
                onRetry?.Invoke(exception, attempt, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task ExecuteAsync(Func<CancellationToken, Task> operation, Func<Exception, bool> isTransient, Action<Exception, int, TimeSpan>? onRetry, TimeSpan baseDelay, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return true;
        }, isTransient, onRetry, baseDelay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Computes the delay that follows <paramref name="attempt"/>: an exponential backoff, plus a jitter of up to 25% so that concurrent callers do not all come back at the same instant.</summary>
    internal static TimeSpan GetDelay(int attempt, TimeSpan baseDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var backoff = baseDelay * Math.Pow(2, attempt - 1);
        var jitterPercentage = RandomNumberGenerator.GetInt32(0, MaxJitterPercentage + 1);
        return backoff + backoff * (jitterPercentage / 100d);
    }
}
