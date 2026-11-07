using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.ResxSourceGenerator;

internal static class ResxGeneratorCommon
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
            // An unset metadata is reported as an empty value, so it must not be considered as a different value
            if (analyzerConfigOptionsProvider.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles." + name, out var value) && !string.IsNullOrEmpty(value))
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
        if (TryGetFullDirectoryPath(projectDir) is not string fullProjectDir)
            return Path.GetFileNameWithoutExtension(resourcePath);

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
        if (TryGetFullDirectoryPath(projectDir) is not string fullProjectDir)
            return null;

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

    /// <summary>
    /// Computes the base name of the generated file. Two resx files can share the same file name, so the path
    /// relative to the project is used to keep the name unique within the generator.
    /// </summary>
    internal static string ComputeHintName(string projectDir, string resourcePath)
    {
        var fullProjectDir = TryGetFullDirectoryPath(projectDir);
        var fullResourcePath = Path.GetFullPath(resourcePath);

        var name = fullProjectDir is not null && fullResourcePath.StartsWith(fullProjectDir, StringComparison.Ordinal)
            ? fullResourcePath[fullProjectDir.Length..]
            : Path.GetFileName(resourcePath);

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(c switch
            {
                '/' or '\\' => '.',
                // Characters allowed in a hint name (Microsoft.CodeAnalysis.AdditionalSourcesCollection)
                '.' or ',' or '-' or '_' or ' ' or '(' or ')' or '[' or ']' => c,
                _ when char.IsLetterOrDigit(c) => c,
                _ => '_',
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// Resolves a directory to a rooted path ending with a separator, or returns <see langword="null"/> when the
    /// value cannot be one. The project directory is unset in projects that reference the analyzer directly
    /// instead of importing the props shipped in the package.
    /// </summary>
    private static string? TryGetFullDirectoryPath(string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return null;

        try
        {
            return EnsureEndSeparator(Path.GetFullPath(directory));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string EnsureEndSeparator(string path)
    {
        if (path[^1] == Path.DirectorySeparatorChar)
            return path;

        return path + Path.DirectorySeparatorChar;
    }

    // Matches the shape of a BCP-47 culture name: a 2 or 3 letter language, an optional 4 letter script,
    // and an optional region that is either 2 letters or 3 digits (zh, fil, zh-Hans, fr-FR, sr-Latn-RS, es-419).
    // The shape is checked instead of asking CultureInfo so that grouping does not depend on the ICU version
    // of the machine running the compiler.
    private const string CultureNamePattern = "^[a-zA-Z]{2,3}(-[a-zA-Z]{4})?(-([a-zA-Z]{2}|[0-9]{3}))?$";

    private static string GetResourceName(string path)
    {
        var pathWithoutExtension = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var indexOf = pathWithoutExtension.LastIndexOf('.', StringComparison.Ordinal);
        if (indexOf < 0)
            return pathWithoutExtension;

        return Regex.IsMatch(pathWithoutExtension[(indexOf + 1)..], CultureNamePattern, RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            ? pathWithoutExtension[0..indexOf]
            : pathWithoutExtension;
    }
}
