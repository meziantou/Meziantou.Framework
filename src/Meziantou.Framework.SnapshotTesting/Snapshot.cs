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
        Validate(value, type: null, settings, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? value, SnapshotType? type, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, type, settings: null, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Validate(object? value, SnapshotType? type, SnapshotSettings? settings, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = -1, [CallerMemberName] string? callerMemberName = null)
    {
        SnapshotEngine.Validate(type, value, settings ?? SnapshotSettings.Default, callerFilePath, callerLineNumber, callerMemberName, TestContext.Value);
    }
}
