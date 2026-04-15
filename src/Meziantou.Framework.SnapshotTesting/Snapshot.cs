using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting;

public static class Snapshot
{
    public static AsyncLocal<SnapshotTestContext?> TestContext { get; } = new();

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? value, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, settings: null, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? value, SnapshotSettings? settings, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        SnapshotEngine.Validate(value, settings ?? SnapshotSettings.Default, filePath, lineNumber, memberName, TestContext.Value);
    }
}

