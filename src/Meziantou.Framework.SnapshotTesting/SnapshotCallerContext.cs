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

    // The names of the base attributes. Attributes deriving from them are recognized too, see IsTestAttributeType.
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
    private int _callOrdinal;

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

    /// <summary>
    /// 1-based position of this assertion among the assertions of its test that the built-in naming strategies
    /// would give the same name. The first assertion keeps the name; the next ones get a suffix, so two calls to
    /// <c>Snapshot.Validate</c> in one test no longer share a snapshot file.
    /// </summary>
    /// <remarks>
    /// The assertions are told apart by their line, see <see cref="SnapshotNameRegistry.GetCallOrdinal" />. Several
    /// assertions made from the same line - a loop, or a helper that does not forward the caller information - still
    /// share a name.
    /// </remarks>
    public int CallOrdinal
    {
        get
        {
            if (_callOrdinal == 0)
            {
                var testContext = _testContext;
                var testKey = string.Join('\0', SourceFilePath.Value, ClassName, MethodName, testContext?.TestName, SnapshotSettings.FormatMetadata(testContext?.Metadata));
                _callOrdinal = SnapshotNameRegistry.GetCallOrdinal(testKey, LineNumber);
            }

            return _callOrdinal;
        }
    }

    /// <summary>
    /// Describes the test the assertion belongs to, to detect two tests sharing a snapshot name.
    /// </summary>
    public SnapshotNameOwner GetNameOwner()
    {
        var testContext = _testContext;
        var metadata = SnapshotSettings.FormatMetadata(testContext?.Metadata);

        // A test name set through Snapshot.TestContext is the user's choice, and several tests may use it to share
        // a snapshot on purpose. Only the name itself tells such assertions apart.
        if (testContext is { IsDetectedFromTestFramework: false, TestName: not null })
            return new SnapshotNameOwner(SourceFilePath: null, ClassName: null, MethodName: null, testContext.TestName, metadata, TestId: null);

        return new SnapshotNameOwner(SourceFilePath.Value, ClassName, MethodName, testContext?.TestName, metadata, testContext?.AmbiguousTestId);
    }

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
            if (IsTestAttributeType(attribute.AttributeType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Indicates whether the attribute marks a test method. Test frameworks and their extensions derive their own
    /// attributes from the base ones - <c>[DataTestMethod]</c>, <c>[STATestMethod]</c>, <c>[SkippableFact]</c>,
    /// <c>[WpfFact]</c>... - so the whole base type chain is checked. Only the names are compared: the library does
    /// not reference any test framework.
    /// </summary>
    private static bool IsTestAttributeType(Type? attributeType)
    {
        try
        {
            while (attributeType is not null && attributeType != typeof(Attribute) && attributeType != typeof(object))
            {
                if (TestAttributeNames.Contains(attributeType.Name))
                    return true;

                attributeType = attributeType.BaseType;
            }
        }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            // A base type may live in an assembly that cannot be loaded.
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
