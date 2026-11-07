namespace Meziantou.Framework.Win32;

internal sealed class ReadChangeJournalOptions(Usn? initialUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout, bool unprivileged)
{
    /// <summary>
    /// The value passed to <c>FSCTL_READ_USN_JOURNAL</c> when the caller asks to wait indefinitely.
    /// The control code has no dedicated "infinite" value, so the largest practical timeout is used instead.
    /// </summary>
    private const ulong InfiniteTimeoutInSeconds = uint.MaxValue;

    public Usn? InitialUSN { get; } = initialUSN;
    public ChangeReason ReasonFilter { get; } = reasonFilter;
    public bool ReturnOnlyOnClose { get; } = returnOnlyOnClose;
    public bool Unprivileged { get; } = unprivileged;
    public TimeSpan Timeout { get; } = timeout;
    public ushort MinimumMajorVersion { get; set; } = 2;
    public ushort MaximumMajorVersion { get; set; } = 4;

    /// <summary>
    /// Gets <see cref="Timeout"/> in the form expected by <c>FSCTL_READ_USN_JOURNAL</c>, whose timeout is a whole number of seconds.
    /// <see cref="TimeSpan.Zero"/> maps to <c>0</c>, meaning the read returns as soon as the journal is exhausted instead of waiting.
    /// A negative value, such as <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, waits indefinitely.
    /// Any other value is rounded up to a whole second, so a sub-second timeout waits for one second rather than not waiting at all.
    /// </summary>
    public ulong TimeoutInSeconds => Timeout switch
    {
        { Ticks: 0 } => 0,
        { Ticks: < 0 } => InfiniteTimeoutInSeconds,
        var value => (ulong)Math.Max(1, Math.Ceiling(value.TotalSeconds)),
    };

    /// <summary>
    /// Gets the value passed to <c>FSCTL_READ_USN_JOURNAL</c> as <c>BytesToWaitFor</c>, which is the amount of new data the
    /// read waits for once it reaches the end of the journal. The control code ignores <see cref="TimeoutInSeconds"/> entirely
    /// while this is <c>0</c>, so a read that is meant to wait has to ask for at least one byte. Asking for a single byte
    /// makes the read return as soon as anything is appended rather than after a fixed amount of new data.
    /// </summary>
    public ulong BytesToWaitFor => TimeoutInSeconds is 0 ? 0ul : 1ul;
}
