using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.ResxSourceGenerator;

internal static partial class ResxGeneratorCommon
{
    internal static IEnumerable<IGrouping<string, AdditionalText>> GetResxGroups(IEnumerable<AdditionalText> files)
    {
        return files
            .GroupBy(file => GetResourceName(file.Path), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.Ordinal);
    }

    internal static bool ParseBoolean(string? value, bool defaultValue)
    {
        if (bool.TryParse(value, out var result))
            return result;
        return defaultValue;
    }

    internal static bool IsValidResxFile(AdditionalText entry, CancellationToken cancellationToken)
    {
        var content = entry.GetText(cancellationToken);
        if (content is null)
            return true;

        try
        {
            _ = XDocument.Parse(content.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string? GetMetadataValue(AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider, string name, string? globalName, IEnumerable<AdditionalText> additionalFiles, out string? inconsistentFilePath)
    {
        inconsistentFilePath = null;
        string? result = null;
        foreach (var file in additionalFiles)
        {
            if (analyzerConfigOptionsProvider.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles." + name, out var value))
            {
                if (result is not null && value != result)
                {
                    inconsistentFilePath = file.Path;
                    return null;
                }

                result = value;
            }
        }

        if (!string.IsNullOrEmpty(result))
            return result;

        if (globalName is not null && analyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property." + globalName, out var globalValue) && !string.IsNullOrEmpty(globalValue))
            return globalValue;

        return null;
    }

    internal static string? ComputeResourceName(string rootNamespace, string projectDir, string resourcePath)
    {
        var fullProjectDir = EnsureEndSeparator(Path.GetFullPath(projectDir));
        var fullResourcePath = Path.GetFullPath(resourcePath);

        if (fullProjectDir == fullResourcePath)
            return rootNamespace;

        if (fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal))
        {
            var relativePath = fullResourcePath[fullProjectDir.Length..];
            return rootNamespace + '.' + relativePath.Replace('/', '.').Replace('\\', '.');
        }

        return Path.GetFileNameWithoutExtension(resourcePath);
    }

    internal static string? ComputeNamespace(string rootNamespace, string projectDir, string resourcePath)
    {
        var fullProjectDir = EnsureEndSeparator(Path.GetFullPath(projectDir));
        var fullResourcePath = EnsureEndSeparator(Path.GetDirectoryName(Path.GetFullPath(resourcePath))!);

        if (fullProjectDir == fullResourcePath)
            return rootNamespace;

        if (fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal))
        {
            var relativePath = fullResourcePath[fullProjectDir.Length..];
            return rootNamespace + '.' + relativePath.Replace('/', '.').Replace('\\', '.').TrimEnd('.');
        }

        return null;
    }

    private static string EnsureEndSeparator(string path)
    {
        if (path[^1] == Path.DirectorySeparatorChar)
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    private static string GetResourceName(string path)
    {
        var pathWithoutExtension = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var indexOf = pathWithoutExtension.LastIndexOf('.', StringComparison.Ordinal);
        if (indexOf < 0)
            return pathWithoutExtension;

#if NET7_0_OR_GREATER
        var cultureSuffix = pathWithoutExtension.AsSpan(indexOf + 1);
#else
        var cultureSuffix = pathWithoutExtension[(indexOf + 1)..];
#endif

        return IsCultureSuffix(cultureSuffix)
            ? pathWithoutExtension[0..indexOf]
            : pathWithoutExtension;
    }

#if NET7_0_OR_GREATER
    private static bool IsCultureSuffix(ReadOnlySpan<char> value) => CultureSuffixRegex().IsMatch(value);

    [GeneratedRegex("^[a-zA-Z]{2}(-[a-zA-Z]{2})?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CultureSuffixRegex();
#else
    private static bool IsCultureSuffix(string value) => Regex.IsMatch(value, "^[a-zA-Z]{2}(-[a-zA-Z]{2})?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
#endif
}
