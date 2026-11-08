using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>The work limits of a diff: its cancellation token, its deadline and the maximum edit cost of the Myers search.</summary>
internal sealed class DiffBudget
{
    private readonly long _deadline;
    private readonly CancellationToken _cancellationToken;
    private bool _expired;

    private DiffBudget(long deadline, int maxEditCost, CancellationToken cancellationToken)
    {
        _deadline = deadline;
        MaxEditCost = maxEditCost;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Gets the maximum edit cost of a region searched by Myers, or <see cref="int.MaxValue"/> when there is no limit.</summary>
    internal int MaxEditCost { get; }

    internal static DiffBudget Create(TextDiffOptions options, CancellationToken cancellationToken)
    {
        // Identical texts never reach a check, so check the token once up front
        cancellationToken.ThrowIfCancellationRequested();

        var deadline = long.MaxValue;
        if (options.Timeout is { } timeout && timeout != Timeout.InfiniteTimeSpan)
        {
            var start = Stopwatch.GetTimestamp();
            var ticks = timeout.TotalSeconds * Stopwatch.Frequency;
            deadline = ticks >= long.MaxValue - start ? long.MaxValue : start + (long)ticks;
        }

        return new DiffBudget(deadline, options.MaxEditCost ?? int.MaxValue, cancellationToken);
    }

    /// <summary>
    /// Throws when the diff is canceled, and returns <see langword="true"/> once the time limit is reached: the regions
    /// that are not diffed yet are then reported as replaced.
    /// </summary>
    internal bool IsExpired()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_expired)
            return true;

        if (_deadline != long.MaxValue && Stopwatch.GetTimestamp() >= _deadline)
        {
            _expired = true;
        }

        return _expired;
    }
}
