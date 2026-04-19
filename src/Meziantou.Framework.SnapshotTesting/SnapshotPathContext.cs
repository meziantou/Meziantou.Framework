namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotPathContext(
    FullPath SourceFilePath,
    string MethodName,
    string? MemberName,
    int LineNumber,
    SnapshotType Type,
    int Index,
    string? Extension,
    SnapshotTestContext? TestContext,
    SnapshotSettings Settings,
    int SnapshotCount = 1);

