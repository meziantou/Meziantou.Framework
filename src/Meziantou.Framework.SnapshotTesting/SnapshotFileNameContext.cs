using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotFileNameContext(
    FullPath SourceFilePath,
    string MethodName,
    string? MemberName,
    int LineNumber,
    SnapshotType Type,
    int Index,
    string? Extension,
    SnapshotTestContext? TestContext,
    SnapshotSettings Settings);

