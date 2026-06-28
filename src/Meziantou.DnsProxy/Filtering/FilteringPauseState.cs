namespace Meziantou.DnsProxy.Filtering;

internal sealed class FilteringPauseState(TimeProvider timeProvider)
{
    private long _disabledUntilUtcTicks;

    public DateTimeOffset? DisabledUntilUtc
    {
        get
        {
            var ticks = Volatile.Read(ref _disabledUntilUtcTicks);
            if (ticks is 0)
            {
                return null;
            }

            var disabledUntilUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (disabledUntilUtc <= timeProvider.GetUtcNow())
            {
                Interlocked.CompareExchange(ref _disabledUntilUtcTicks, 0, ticks);
                return null;
            }

            return disabledUntilUtc;
        }
    }

    public bool IsDisabled => DisabledUntilUtc is not null;

    public DateTimeOffset DisableFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must be greater than zero.");

        var disabledUntilUtc = timeProvider.GetUtcNow().Add(duration);
        Volatile.Write(ref _disabledUntilUtcTicks, disabledUntilUtc.UtcDateTime.Ticks);

        return disabledUntilUtc;
    }
}
