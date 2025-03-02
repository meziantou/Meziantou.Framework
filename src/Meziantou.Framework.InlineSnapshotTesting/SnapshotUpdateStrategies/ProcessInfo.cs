namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed partial record ProcessInfo
{
    public int ProcessId { get; init; }
    public string? ProcessName { get; init; }

    // Process Id can be reused, so you need to also check the start time
    public DateTimeOffset ProcessStartedAt { get; init; }
}
