using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed record SnapshotCallerContext(FullPath SourceFilePath, string MethodName, string? ContainingTypeName, string? MemberName, int LineNumber)
{
    private static readonly Regex LambdaContainingMethodNameRegex = new(@"^<(?<name>[^>]+)>b__[0-9]+(_[0-9]+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, matchTimeout: Timeout.InfiniteTimeSpan);

    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        "FactAttribute",
        "TheoryAttribute",
        "TestAttribute",
        "TestMethodAttribute",
    };

    public static SnapshotCallerContext Create(string? filePath, int lineNumber, string? memberName)
    {
        var stackTrace = new StackTrace(fNeedFileInfo: true);
        var stackAnalysisStartIndex = GetStackAnalysisStartIndex(stackTrace);
        string? discoveredMethodName = null;
        string? discoveredContainingTypeName = null;

        for (var i = stackAnalysisStartIndex; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame?.GetMethod();
            if (method is null)
                continue;

            method = CallerContextUtilities.ResolveActualMethod(method);

            if (HasTestAttribute(method))
            {
                discoveredMethodName = NormalizeMethodName(method.Name);
                discoveredContainingTypeName = NormalizeTypeName(method.DeclaringType);
                break;
            }

            discoveredMethodName ??= NormalizeMethodName(method.Name);
            discoveredContainingTypeName ??= NormalizeTypeName(method.DeclaringType);
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
        for (var i = stackTrace.FrameCount - 1; i >= 0; i--)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame?.GetMethod();
            if (method is null)
                continue;

            method = CallerContextUtilities.ResolveActualMethod(method);
            if (method.GetCustomAttribute<SnapshotAssertionAttribute>(inherit: false) is not null)
                return i + 1;
        }

        return 0;
    }

    internal static FullPath ResolveSourceFilePath(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (CallerContextUtilities.TryResolveSourceFilePath(sourceFilePath, out var resolvedSourceFilePath))
            return resolvedSourceFilePath;

        throw new SnapshotException($"Cannot find source file path '{sourceFilePath}'.");
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
}
