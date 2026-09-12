using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Describes the call site of an assertion. The names of the test class and of the test method come from the
/// test framework when it exposes them, and from the call stack otherwise. The walk is the most expensive
/// part of an assertion, so it only runs when something asks for a name the test framework did not provide.
/// </summary>
internal sealed partial class SnapshotCallerContext
{
    [GeneratedRegex(@"^<(?<name>[^>]+)>b__[0-9]+(_[0-9]+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: -1)]
    private static partial Regex LambdaContainingMethodNameRegex { get; }

    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        "FactAttribute",
        "TheoryAttribute",
        "TestAttribute",
        "TestMethodAttribute",
    };

    /// <summary>
    /// Caches everything the stack walk needs to know about a method. Resolving the state machine,
    /// reading the attributes and normalizing the names are pure functions of the method, and the same
    /// methods show up on the stack of every single assertion.
    /// </summary>
    private static readonly ConcurrentDictionary<MethodBase, StackFrameMethod> StackFrameMethods = new();

    private readonly SnapshotTestContext? _testContext;
    private StackWalkResult? _stackWalk;

    private SnapshotCallerContext(string? filePath, int lineNumber, string? memberName, SnapshotTestContext? testContext)
    {
        LineNumber = lineNumber;
        MemberName = memberName;
        _testContext = testContext;

        if (filePath is not null)
        {
            SourceFilePath = ResolveSourceFilePath(filePath);
            return;
        }

        // The call stack is the only remaining source for the file path, so the walk is not optional here.
        // It also needs the file name of the frames, which forces the PDBs to be loaded and the sequence
        // points to be decoded.
        var stackWalk = WalkStack(needFileInfo: true);
        _stackWalk = stackWalk;
        if (stackWalk.SourceFilePath is null)
            throw new SnapshotException("Cannot find the file to update from the call stack. The PDB may be missing.");

        SourceFilePath = ResolveSourceFilePath(stackWalk.SourceFilePath);
    }

    public FullPath SourceFilePath { get; }

    public int LineNumber { get; }

    /// <summary>
    /// Member name provided by the compiler at the call site. It is always available and never needs the
    /// call stack.
    /// </summary>
    public string? MemberName { get; }

    /// <summary>
    /// Name of the test method the assertion belongs to. The test framework is asked first: its view is free,
    /// and it is the only one that survives an await in a helper method, which drops the test method from
    /// the call stack. The stack is walked only when the framework exposes nothing (Xunit v2, MSTest, or no
    /// test framework at all), the first time the name is read.
    /// </summary>
    public string MethodName
    {
        get
        {
            if (_testContext?.MethodName is { } methodName)
                return methodName;

            return ResolveStackWalk().MethodName ?? MemberName ?? "Snapshot";
        }
    }

    /// <summary>
    /// Simple name of the type declaring the test method. Same sources and precedence as <see cref="MethodName" />.
    /// </summary>
    public string? ClassName => _testContext?.ClassName ?? ResolveStackWalk().ClassName;

    /// <summary>Indicates whether the call stack was actually walked. Used by the tests.</summary>
    internal bool StackWalkPerformed => _stackWalk is not null && !ReferenceEquals(_stackWalk, StackWalkResult.Unavailable);

    public static SnapshotCallerContext Create(string? filePath, int lineNumber, string? memberName, SnapshotTestContext? testContext)
    {
        return new SnapshotCallerContext(filePath, lineNumber, memberName, testContext);
    }

    /// <summary>
    /// Prevents any further stack walk. The frames the walk looks for are only on the stack while the
    /// assertion is running, so a context that outlives its assertion - a strategy may keep the
    /// <see cref="SnapshotPathContext" /> it received - reports what the test framework knows instead of
    /// describing an unrelated call stack.
    /// </summary>
    public void Freeze() => _stackWalk ??= StackWalkResult.Unavailable;

    private StackWalkResult ResolveStackWalk() => _stackWalk ??= WalkStack(needFileInfo: false);

    private static StackWalkResult WalkStack(bool needFileInfo)
    {
        var stackTrace = new StackTrace(needFileInfo);
        var stackAnalysisStartIndex = GetStackAnalysisStartIndex(stackTrace);
        string? discoveredMethodName = null;
        string? discoveredClassName = null;

        for (var i = stackAnalysisStartIndex; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame?.GetMethod();
            if (method is null)
                continue;

            var stackFrameMethod = GetStackFrameMethod(method);

            if (stackFrameMethod.IsTestMethod)
            {
                discoveredMethodName = stackFrameMethod.NormalizedMethodName;
                discoveredClassName = stackFrameMethod.NormalizedTypeName;
                break;
            }

            discoveredMethodName ??= stackFrameMethod.NormalizedMethodName;
            discoveredClassName ??= stackFrameMethod.NormalizedTypeName;
        }

        string? sourceFilePath = null;
        if (needFileInfo)
        {
            for (var i = stackAnalysisStartIndex; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var candidateFilePath = frame?.GetFileName();
                if (candidateFilePath is not null)
                {
                    sourceFilePath = candidateFilePath;
                    break;
                }
            }
        }

        return new StackWalkResult(discoveredMethodName, discoveredClassName, sourceFilePath);
    }

    private static int GetStackAnalysisStartIndex(StackTrace stackTrace)
    {
        // The methods decorated with [SnapshotAssertion] form a contiguous run in the middle of the stack:
        // the attribute is internal and is only applied to the Snapshot.Validate overloads, which forward to
        // each other. Walking outwards and stopping right after that run avoids reflecting over the whole
        // stack, which is mostly test framework frames. The frames below the run - the engine, and whatever
        // strategy asked for a name - are skipped by the same rule, which is what makes the walk deferrable.
        var startIndex = 0;
        for (var i = 0; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame?.GetMethod();
            if (method is null)
                continue;

            if (GetStackFrameMethod(method).IsSnapshotAssertion)
            {
                startIndex = i + 1;
            }
            else if (startIndex > 0)
            {
                break;
            }
        }

        return startIndex;
    }

    internal static FullPath ResolveSourceFilePath(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (CallerContextUtilities.TryResolveSourceFilePath(sourceFilePath, out var resolvedSourceFilePath))
            return resolvedSourceFilePath;

        throw new SnapshotException($"Cannot find source file path '{sourceFilePath}'.");
    }

    private static StackFrameMethod GetStackFrameMethod(MethodBase method)
    {
        return StackFrameMethods.GetOrAdd(method, static method =>
        {
            var resolvedMethod = CallerContextUtilities.ResolveActualMethod(method);
            return new StackFrameMethod(
                IsSnapshotAssertion: resolvedMethod.GetCustomAttribute<SnapshotAssertionAttribute>(inherit: false) is not null,
                IsTestMethod: HasTestAttribute(resolvedMethod),
                NormalizedMethodName: NormalizeMethodName(resolvedMethod.Name),
                NormalizedTypeName: NormalizeTypeName(resolvedMethod.DeclaringType));
        });
    }

    private static bool HasTestAttribute(MethodBase method)
    {
        IList<CustomAttributeData> attributes;
        try
        {
            attributes = method.GetCustomAttributesData();
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (NotImplementedException)
        {
            return false;
        }

        foreach (var attribute in attributes)
        {
            var attributeName = attribute.AttributeType.Name;
            if (TestAttributeNames.Contains(attributeName))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeMethodName(string name)
    {
        if (TryGetLambdaContainingMethodName(name, out var lambdaContainingMethodName))
            return lambdaContainingMethodName;

        if (CallerContextUtilities.TryParseLocalFunctionName(name, out var localFunctionName))
            return localFunctionName;

        return name;
    }

    private static bool TryGetLambdaContainingMethodName(string name, [NotNullWhen(true)] out string? containingMethodName)
    {
        containingMethodName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var match = LambdaContainingMethodNameRegex.Match(name);
        if (!match.Success)
            return false;

        containingMethodName = match.Groups["name"].Value;
        return !string.IsNullOrEmpty(containingMethodName);
    }

    private static string? NormalizeTypeName(Type? type)
    {
        while (type is not null)
        {
            var typeName = type.Name;
            if (!string.IsNullOrWhiteSpace(typeName) && !IsCompilerGeneratedTypeName(typeName))
            {
                var genericSeparatorIndex = typeName.IndexOf('`', StringComparison.Ordinal);
                if (genericSeparatorIndex < 0)
                    return typeName;

                return typeName[..genericSeparatorIndex];
            }

            type = type.DeclaringType;
        }

        return null;
    }

    private static bool IsCompilerGeneratedTypeName(string typeName)
    {
        return typeName.StartsWith("<", StringComparison.Ordinal);
    }

    private sealed record StackFrameMethod(bool IsSnapshotAssertion, bool IsTestMethod, string NormalizedMethodName, string? NormalizedTypeName);

    private sealed record StackWalkResult(string? MethodName, string? ClassName, string? SourceFilePath)
    {
        /// <summary>Result of a walk that cannot run because the frames of the assertion are gone.</summary>
        public static StackWalkResult Unavailable { get; } = new(MethodName: null, ClassName: null, SourceFilePath: null);
    }
}
