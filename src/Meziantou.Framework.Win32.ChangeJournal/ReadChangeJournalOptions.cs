namespace Meziantou.Framework.Win32;

internal sealed class ReadChangeJournalOptions(Usn? initialUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout, bool unprivileged)
{
    public Usn? InitialUSN { get; } = initialUSN;
    public ChangeReason ReasonFilter { get; } = reasonFilter;
    public bool ReturnOnlyOnClose { get; } = returnOnlyOnClose;
    public bool Unprivileged { get; } = unprivileged;
    public TimeSpan Timeout { get; } = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
    public ushort MinimumMajorVersion { get; set; } = 2;
    public ushort MaximumMajorVersion { get; set; } = 4;
}
