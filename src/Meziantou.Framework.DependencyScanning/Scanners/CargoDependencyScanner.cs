using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Cargo manifests and lockfiles for Rust crate dependencies.</summary>
public sealed partial class CargoDependencyScanner : DependencyScanner
{
    private static readonly string[] DependencySections =
    [
        "dependencies",
        "dev-dependencies",
        "build-dependencies",
        "workspace.dependencies",
    ];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RustCrate];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Cargo.toml", ignoreCase: false)
            || context.HasFileName("Cargo.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (Path.GetFileName(context.FullPath).Equals("Cargo.lock", StringComparison.Ordinal))
        {
            await ScanLockFileAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanManifestAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanManifestAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = TomlSyntaxTree.ParseText(text, context.FullPath);
        foreach (var table in tree.GetRoot().Tables)
        {
            if (table.IsArrayOfTables || !DependencySections.Contains(TomlUtilities.GetName(table.Key), StringComparer.Ordinal))
                continue;

            foreach (var property in table.Properties)
            {
                var names = property.Key.Names;
                TomlStringSyntax? version;
                if (names.Count is 1)
                {
                    // serde = "1.0", serde = { version = "1.0" } or serde = { path = "../serde" }
                    version = TomlUtilities.GetString(property.Value) ?? TomlUtilities.GetInlineTableString(property.Value, "version");
                }
                else if (names is [_, "version"] && TomlUtilities.GetString(property.Value) is { } dottedVersion)
                {
                    // serde.version = "1.0". Other dotted keys, such as serde.workspace = true, are not versions.
                    version = dottedVersion;
                }
                else
                {
                    continue;
                }

                var nameSpan = property.Key.Parts[0].Span;
                if (property.Key.Parts[0].Text is ['"' or '\'', ..])
                {
                    nameSpan = new TextSpan(nameSpan.Start + 1, nameSpan.Length - 2);
                }

                Location versionLocation = version is not null ? TomlUtilities.CreateLocation(context, tree, version) : new NonUpdatableLocation(context);
                context.ReportDependency(this, names[0], version?.Value, DependencyType.RustCrate, TomlUtilities.CreateLocation(context, tree, nameSpan), versionLocation);
            }
        }
    }

    private async ValueTask ScanLockFileAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? packageName = null;
        var packageNameLine = 0;
        var packageNameColumn = 0;
        var inPackage = false;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            if (line.Trim() is "[[package]]")
            {
                inPackage = true;
                packageName = null;
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                inPackage = false;
                packageName = null;
            }

            if (!inPackage)
                continue;

            var nameMatch = LockPackageNameRegex().Match(line);
            if (nameMatch.Success)
            {
                packageName = nameMatch.Groups["name"].Value;
                packageNameLine = lineNumber;
                packageNameColumn = nameMatch.Groups["name"].Index + 1;
                continue;
            }

            if (packageName is null)
                continue;

            var versionMatch = LockPackageVersionRegex().Match(line);
            if (!versionMatch.Success)
                continue;

            // The version is not updatable as the checksum of the package would not match anymore
            context.ReportDependency(this, packageName, versionMatch.Groups["version"].Value, DependencyType.RustCrate,
                new TextLocation(context.FileSystem, context.FullPath, packageNameLine, packageNameColumn, packageName.Length),
                new NonUpdatableLocation(context));
            packageName = null;
        }
    }

    [GeneratedRegex("""^\s*name\s*=\s*"(?<name>[^"]+)"\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockPackageNameRegex();

    [GeneratedRegex("""^\s*version\s*=\s*"(?<version>[^"]+)"\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockPackageVersionRegex();
}
