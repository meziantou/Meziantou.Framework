using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed partial record SnapshotCallerContext(FullPath SourceFilePath, string MethodName, string? ContainingTypeName, string? MemberName, int LineNumber)
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

    public static SnapshotCallerContext Create(string? filePath, int lineNumber, string? memberName)
    {
        // Resolving the file name of every frame forces the PDBs to be loaded and the sequence points to
        // be decoded. It is only needed when the caller did not provide a file path.
        var stackTrace = new StackTrace(fNeedFileInfo: filePath is null);
        var stackAnalysisStartIndex = GetStackAnalysisStartIndex(stackTrace);
        string? discoveredMethodName = null;
        string? discoveredContainingTypeName = null;

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
                discoveredContainingTypeName = stackFrameMethod.NormalizedTypeName;
                break;
            }

            discoveredMethodName ??= stackFrameMethod.NormalizedMethodName;
            discoveredContainingTypeName ??= stackFrameMethod.NormalizedTypeName;
        }

        var sourceFilePath = filePath;
        if (sourceFilePath is null)
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

        if (sourceFilePath is null)
            throw new SnapshotException("Cannot find the file to update from the call stack. The PDB may be missing.");

        discoveredMethodName ??= memberName ?? "Snapshot";
        return new SnapshotCallerContext(ResolveSourceFilePath(sourceFilePath), discoveredMethodName, discoveredContainingTypeName, memberName, lineNumber);
    }

    private static int GetStackAnalysisStartIndex(StackTrace stackTrace)
    {
        // The methods decorated with [SnapshotAssertion] form a contiguous run at the innermost end of the
        // stack: the attribute is internal and is only applied to the Snapshot.Validate overloads, which
        // forward to each other. Walking outwards and stopping right after that run avoids reflecting over
        // the whole stack, which is mostly test framework frames.
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
}
