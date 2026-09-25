using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans modern Python project manifests and lockfiles for PyPI packages.</summary>
public sealed partial class PythonProjectDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PyPi];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("pyproject.toml", ignoreCase: false)
            || context.HasFileName("poetry.lock", ignoreCase: false)
            || context.HasFileName("Pipfile", ignoreCase: false)
            || context.HasFileName("Pipfile.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var fileName = Path.GetFileName(context.FullPath);
        if (fileName.Equals("poetry.lock", StringComparison.Ordinal))
        {
            await ScanPoetryLockAsync(context).ConfigureAwait(false);
        }
        else if (fileName.Equals("Pipfile.lock", StringComparison.Ordinal))
        {
            await ScanJsonLockAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanManifestAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanManifestAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var match = ManifestDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"];
            var version = match.Groups["version"];
            context.ReportDependency(this, name.Value, version.Value, DependencyType.PyPi,
                new NonUpdatableLocation(context),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
        }
    }

    private async ValueTask ScanPoetryLockAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        var inPackage = false;
        string? name = null;
        var nameLine = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            if (line.Trim() is "[[package]]")
            {
                inPackage = true;
                name = null;
                continue;
            }

            if (!inPackage)
                continue;

            var nameMatch = LockNameRegex().Match(line);
            if (nameMatch.Success)
            {
                name = nameMatch.Groups["name"].Value;
                nameLine = lineNumber;
                continue;
            }

            if (name is null)
                continue;

            var versionMatch = LockVersionRegex().Match(line);
            if (!versionMatch.Success)
                continue;

            var version = versionMatch.Groups["version"];
            context.ReportDependency(this, name, version.Value, DependencyType.PyPi,
                new NonUpdatableLocation(context),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
            name = null;
        }
    }

    private async ValueTask ScanJsonLockAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNode.ParseAsync(context.Content, cancellationToken: context.CancellationToken).ConfigureAwait(false);
            if (doc is not JsonObject root)
                return;

            foreach (var sectionName in new[] { "default", "develop" })
            {
                if (root[sectionName] is not JsonObject section)
                    continue;

                foreach (var dependency in section)
                {
                    if (dependency.Value is not JsonObject package || package["version"]?.GetValue<string>() is not { } version)
                        continue;

                    context.ReportDependency(this, dependency.Key, version, DependencyType.PyPi,
                        new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    [GeneratedRegex("""^\s*["']?(?<name>[A-Za-z0-9][A-Za-z0-9_.-]*)["']?\s*(?:==|=)\s*["']?(?:==)?(?<version>[A-Za-z0-9][A-Za-z0-9_.!+*-]*)["']?""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ManifestDependencyRegex();

    [GeneratedRegex("""^\s*name\s*=\s*"(?<name>[^"]+)"\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockNameRegex();

    [GeneratedRegex("""^\s*version\s*=\s*"(?<version>[^"]+)"\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockVersionRegex();
}
