using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotPathContext(
    FullPath SourceFilePath,
    string MethodName,
    string? MemberName,
    int LineNumber,
    SnapshotType Type,
    int Index,
    string FileName,
    SnapshotTestContext? TestContext,
    SnapshotSettings Settings);

