using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Snapshot data is binary content used by file APIs and diff tooling.")]
public sealed record SnapshotData(string? Extension, byte[] Data);

