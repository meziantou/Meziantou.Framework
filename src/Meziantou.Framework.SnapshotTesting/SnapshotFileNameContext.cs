using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotFileNameContext(
    FullPath SourceFilePath,
    string MethodName,
    int LineNumber,
    SnapshotType Type,
    int Index,
    SnapshotTestContext? TestContext,
    SnapshotSettings Settings);

