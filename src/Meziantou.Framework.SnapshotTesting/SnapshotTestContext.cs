namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotTestContext(
    string? TestName = null,
    string? Parameters = null,
    IReadOnlyDictionary<string, string?>? Metadata = null);

