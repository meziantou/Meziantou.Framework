using System.ComponentModel;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>A snapshot file and its content, as produced by an assertion.</summary>
/// <remarks>This type is only used internally and will no longer be public in the next major version.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SnapshotFile(FullPath FilePath, SnapshotData Data);
