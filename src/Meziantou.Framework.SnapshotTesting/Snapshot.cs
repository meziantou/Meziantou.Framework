using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting;

public static class Snapshot
{
    public static AsyncLocal<SnapshotTestContext?> TestContext { get; } = new();

    /// <summary>
    /// Registers a deterministic source-root mapping so generated paths (for example <c>/_/</c>)
    /// can be resolved back to real source files on disk.
    /// </summary>
    /// <param name="mappedPath">Deterministic mapped prefix emitted by the compiler.</param>
    /// <param name="realPath">Real source-root path on disk.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterSourceRootMapping(string mappedPath, string realPath)
    {
        CallerContextUtilities.RegisterSourceRootMapping(mappedPath, realPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [OverloadResolutionPriority(-1)] // Snapshot type has an implicit conversion from string, so this method must be called only if the user explicitly specify the type of snapshot
    [SnapshotAssertion]
    public static void Validate(object? value, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, settings: null, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static void Validate(object? value, SnapshotSettings? settings, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, type: null, settings, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static void Validate(object? value, SnapshotType? type, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1, [CallerMemberName] string? memberName = null)
    {
        Validate(value, type, settings: null, filePath, lineNumber, memberName);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    [SnapshotAssertion]
    public static void Validate(object? value, SnapshotType? type, SnapshotSettings? settings, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = -1, [CallerMemberName] string? callerMemberName = null)
    {
        SnapshotEngine.Validate(type, value, settings ?? SnapshotSettings.Default, callerFilePath, callerLineNumber, callerMemberName, TestContext.Value);
    }
}
