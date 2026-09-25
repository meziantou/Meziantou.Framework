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
        var isDependencySection = false;
        foreach (var entry in tree.GetRoot().Entries)
        {
            if (entry is TomlTableSyntax table)
            {
                isDependencySection = DependencySections.Contains(table.Name, StringComparer.Ordinal);
                continue;
            }

            if (!isDependencySection || entry is not TomlPropertySyntax property || !property.ValueNode.IsToken)
                continue;

            var name = property.Key;
            var value = property.Value;
            string? version;
            int versionOffset;
            if (name.Contains('.', StringComparison.Ordinal))
            {
                // Dotted keys, such as serde.version = "1.0" or serde.workspace = true
                const string VersionSuffix = ".version";
                if (!name.EndsWith(VersionSuffix, StringComparison.Ordinal) || !TomlUtilities.TryGetString(value, out version, out versionOffset))
                    continue;

                name = name[..^VersionSuffix.Length];
            }
            else if (!TomlUtilities.TryGetString(value, out version, out versionOffset))
            {
                // version is null when the inline table has no version, such as { path = "../local" } or { workspace = true }
                _ = TomlUtilities.TryGetInlineTableString(value, "version", out version, out versionOffset);
            }

            var nameLocation = TomlUtilities.CreateLocation(context, tree, new TextSpan(property.KeyToken.Span.Start, name.Length));
            Location versionLocation = version is not null
                ? TomlUtilities.CreateLocation(context, tree, new TextSpan(property.ValueNode.AsToken().Span.Start + versionOffset, version.Length))
                : new NonUpdatableLocation(context);
            context.ReportDependency(this, name, version, DependencyType.RustCrate, nameLocation, versionLocation);
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
