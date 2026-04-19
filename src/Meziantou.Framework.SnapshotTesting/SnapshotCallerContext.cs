using System.Diagnostics;
using System.Reflection;

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
        return new SnapshotCallerContext(ResolveSourceFilePath(sourceFilePath), discoveredMethodName, memberName, lineNumber);
    }

    internal static FullPath ResolveSourceFilePath(string sourceFilePath)
    {
        return ResolveSourceFilePath(sourceFilePath, EnumeratePotentialSourceRoots());
    }

    internal static FullPath ResolveSourceFilePath(string sourceFilePath, IEnumerable<string?> sourceRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentNullException.ThrowIfNull(sourceRoots);

        var sourcePath = FullPath.FromPath(sourceFilePath);
        if (File.Exists(sourcePath))
            return sourcePath;

        var relativePath = TryGetPathMappedRelativePath(sourcePath.Value);
        if (relativePath is null)
            return sourcePath;

        var normalizedRelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        foreach (var sourceRoot in sourceRoots)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                continue;

            var candidatePath = FullPath.FromPath(sourceRoot) / normalizedRelativePath;
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        return sourcePath;
    }

    private static string? TryGetPathMappedRelativePath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (normalizedPath.StartsWith("/_/", StringComparison.Ordinal))
            return normalizedPath[3..];

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return null;

        var remainder = path[root.Length..].Replace('\\', '/');
        if (remainder.StartsWith("_/", StringComparison.Ordinal))
            return remainder[2..];

        return null;
    }

    private static IEnumerable<string?> EnumeratePotentialSourceRoots()
    {
        yield return Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        yield return Environment.GetEnvironmentVariable("BUILD_SOURCESDIRECTORY");
        yield return Environment.GetEnvironmentVariable("SYSTEM_DEFAULTWORKINGDIRECTORY");
        yield return Environment.GetEnvironmentVariable("CI_PROJECT_DIR");
        yield return Environment.GetEnvironmentVariable("MF_CurrentDirectory");
        yield return Environment.CurrentDirectory;
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
}
