using System.Diagnostics;
using System.Reflection;
using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed record SnapshotCallerContext(FullPath SourceFilePath, string MethodName, string? MemberName, int LineNumber)
{
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
        string? discoveredMethodName = null;

        for (var i = 0; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame?.GetMethod();
            if (method is null)
                continue;

            var declaringType = method.DeclaringType;
            if (declaringType?.Namespace?.StartsWith("Meziantou.Framework.SnapshotTesting", StringComparison.Ordinal) == true)
                continue;

            if (HasTestAttribute(method))
            {
                discoveredMethodName = method.Name;
                break;
            }

            discoveredMethodName ??= method.Name;
        }

        var sourceFilePath = filePath;
        if (sourceFilePath is null)
        {
            for (var i = 0; i < stackTrace.FrameCount; i++)
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
        return new SnapshotCallerContext(FullPath.FromPath(sourceFilePath), discoveredMethodName, memberName, lineNumber);
    }

    private static bool HasTestAttribute(MethodBase method)
    {
        foreach (var attribute in method.GetCustomAttributesData())
        {
            var attributeName = attribute.AttributeType.Name;
            if (TestAttributeNames.Contains(attributeName))
            {
                return true;
            }
        }

        return false;
    }
}

