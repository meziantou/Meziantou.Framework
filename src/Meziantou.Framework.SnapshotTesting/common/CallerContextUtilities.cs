using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting;

internal static class CallerContextUtilities
{
    /// <summary>
    /// Newer Roslyn versions use the format "&lt;callerName&gt;g__functionName|x_y".
    /// Older versions use "&lt;callerName&gt;g__functionNamex_y".
    /// </summary>
    /// <see href="https://github.com/dotnet/roslyn/blob/aecd49800750d64e08767836e2678ffa62a4647f/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNames.cs#L109" />
    private static readonly Regex FunctionNameRegex = new(@"^<(.*)>g__(?<name>[^\|]*)\|{0,1}[0-9]+(_[0-9]+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, matchTimeout: Timeout.InfiniteTimeSpan);

    private static readonly ConcurrentDictionary<string, string> SourceRootMappings = new(StringComparer.Ordinal);

    internal static MethodBase ResolveActualMethod(MethodBase method)
    {
        if (method.DeclaringType is null || !method.DeclaringType.IsAssignableTo(typeof(IAsyncStateMachine)))
            return method;

        var parentType = method.DeclaringType.DeclaringType;
        if (parentType is null)
            return method;

        static MethodInfo[] GetDeclaredMethods(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var methods = GetDeclaredMethods(parentType);
        if (methods is null)
            return method;

        foreach (var candidateMethod in methods)
        {
            var attributes = candidateMethod.GetCustomAttributes<StateMachineAttribute>(inherit: false);
            if (attributes is null)
                continue;

            foreach (var stateMachineAttribute in attributes)
            {
                if (stateMachineAttribute.StateMachineType == method.DeclaringType)
                    return candidateMethod;
            }
        }

        return method;
    }

    internal static bool TryParseLocalFunctionName(string name, [NotNullWhen(true)] out string? functionName)
    {
        functionName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var match = FunctionNameRegex.Match(name);
        functionName = match.Groups["name"].Value;
        return match.Success;
    }

    internal static void RegisterSourceRootMapping(string mappedPath, string realPath)
    {
        SourceRootMappings[mappedPath] = realPath;
    }

    internal static bool TryResolveSourceFilePath(string sourceFilePath, [NotNullWhen(true)] out string? resolvedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (File.Exists(sourceFilePath))
        {
            resolvedPath = sourceFilePath;
            return true;
        }

        var relativePath = TryGetPathMappedRelativePath(sourceFilePath);
        if (relativePath is null)
        {
            resolvedPath = null;
            return false;
        }

        var normalizedRelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        foreach (var realRoot in SourceRootMappings.Values)
        {
            var candidatePath = Path.Combine(realRoot, normalizedRelativePath);
            if (File.Exists(candidatePath))
            {
                resolvedPath = candidatePath;
                return true;
            }
        }

        resolvedPath = null;
        return false;
    }

    private static string? TryGetPathMappedRelativePath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');

        string? longestMappedPrefix = null;
        foreach (var mappedPrefix in SourceRootMappings.Keys)
        {
            if (normalizedPath.StartsWith(mappedPrefix, StringComparison.Ordinal) &&
                (longestMappedPrefix is null || mappedPrefix.Length > longestMappedPrefix.Length))
            {
                longestMappedPrefix = mappedPrefix;
            }
        }

        if (longestMappedPrefix is not null)
            return normalizedPath[longestMappedPrefix.Length..];

        // Generic fallbacks for the common /_/ pattern when no mappings are registered
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
}
