namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotPathContext(
    FullPath SourceFilePath,
    string? ClassName,
    string MethodName,
    int LineNumber,
    SnapshotType Type,
    int Index,
    string? Extension,
    SnapshotTestContext? TestContext,
    SnapshotSettings Settings,
    int SnapshotCount = 1);
