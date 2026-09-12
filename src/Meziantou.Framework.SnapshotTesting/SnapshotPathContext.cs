namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotPathContext
{
    private readonly SnapshotCallerContext? _callerContext;
    private readonly string? _className;
    private readonly string? _methodName;

    // The parameter names are part of the public API: this type used to be a positional record, and callers
    // pass them as named arguments.
#pragma warning disable IDE1006 // Naming Styles
    public SnapshotPathContext(
        FullPath SourceFilePath,
        string? ClassName,
        string MethodName,
        int LineNumber,
        SnapshotType Type,
        int Index,
        string? Extension,
        SnapshotTestContext? TestContext,
        SnapshotSettings Settings,
        int SnapshotCount = 1,
        string? MemberName = null)
    {
        this.SourceFilePath = SourceFilePath;
        _className = ClassName;
        _methodName = MethodName;
        this.LineNumber = LineNumber;
        this.Type = Type;
        this.Index = Index;
        this.Extension = Extension;
        this.TestContext = TestContext;
        this.Settings = Settings;
        this.SnapshotCount = SnapshotCount;
        this.MemberName = MemberName;
    }
#pragma warning restore IDE1006 // Naming Styles

    internal SnapshotPathContext(
        SnapshotCallerContext callerContext,
        SnapshotType type,
        int index,
        string? extension,
        SnapshotTestContext? testContext,
        SnapshotSettings settings,
        int snapshotCount)
    {
        _callerContext = callerContext;
        SourceFilePath = callerContext.SourceFilePath;
        LineNumber = callerContext.LineNumber;
        MemberName = callerContext.MemberName;
        Type = type;
        Index = index;
        Extension = extension;
        TestContext = testContext;
        Settings = settings;
        SnapshotCount = snapshotCount;
    }

    public FullPath SourceFilePath { get; init; }

    /// <summary>
    /// Simple name of the type declaring the test, or <see langword="null" /> when it is unknown.
    /// </summary>
    /// <remarks>
    /// Unless the value was provided explicitly, it comes from <see cref="TestContext" /> when the test
    /// framework exposes it, and from the call stack otherwise. The walk is the most expensive part of an
    /// assertion and runs the first time a name the test framework did not provide is read; a strategy that
    /// reads neither this nor <see cref="MethodName" /> never pays for it.
    /// </remarks>
    public string? ClassName
    {
        get => _className ?? _callerContext?.ClassName;
        init => _className = value;
    }

    /// <summary>
    /// Name of the test method the snapshot belongs to.
    /// </summary>
    /// <remarks>
    /// Unless the value was provided explicitly, it comes from <see cref="TestContext" /> when the test
    /// framework exposes it, and from the call stack otherwise. The walk is the most expensive part of an
    /// assertion and runs the first time a name the test framework did not provide is read. Use
    /// <see cref="MemberName" /> when the name the compiler captured at the call site is enough.
    /// </remarks>
    public string MethodName
    {
        get => _methodName ?? _callerContext?.MethodName ?? "";
        init => _methodName = value;
    }

    /// <summary>
    /// Member name captured by the compiler at the call site, or <see langword="null" /> when the assertion
    /// did not provide one. Unlike <see cref="MethodName" />, it never needs the test framework nor the call
    /// stack, but it describes the method that called the assertion rather than the test method.
    /// </summary>
    public string? MemberName { get; init; }

    public int LineNumber { get; init; }

    public SnapshotType Type { get; init; }

    public int Index { get; init; }

    public string? Extension { get; init; }

    public SnapshotTestContext? TestContext { get; init; }

    public SnapshotSettings Settings { get; init; }

    public int SnapshotCount { get; init; }

    /// <summary>Indicates whether reading a name walked the call stack. Used by the tests.</summary>
    internal bool StackWalkPerformed => _callerContext?.StackWalkPerformed ?? false;
}
