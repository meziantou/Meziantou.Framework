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

        // A table can be written with a header, with dotted keys, or as an inline table, so the pairs are matched on
        // the full key they define: [dependencies] serde = "1.0", [dependencies.serde] version = "1.0", and
        // dependencies.serde.version = "1.0" are the same dependency.
        foreach (var pair in tree.GetRoot().GetKeyValues())
        {
            if (pair.Table is { IsArrayOfTables: true } || TomlUtilities.IsInInlineTable(pair) || GetDependencyTableLength(pair.Names) is not (> 0 and var tableLength))
                continue;

            TomlStringSyntax? version;
            if (pair.Names.Count == tableLength + 1)
            {
                // serde = "1.0", serde = { version = "1.0" } or serde = { path = "../serde" }
                if (pair.Value is not (TomlStringSyntax or TomlInlineTableSyntax))
                    continue;

                version = TomlUtilities.GetString(pair.Value) ?? TomlUtilities.GetInlineTableString(pair.Value, "version");
            }
            else if (pair.Names.Count == tableLength + 2 && pair.Names[^1] is "version" && TomlUtilities.GetString(pair.Value) is { } dottedVersion)
            {
                // serde.version = "1.0", or version = "1.0" under [dependencies.serde]. Other keys, such as
                // serde.workspace = true, are not versions.
                version = dottedVersion;
            }
            else
            {
                continue;
            }

            var versionLocation = version is not null ? TomlUtilities.CreateLocation(context, tree, version) : new NonUpdatableLocation(context);
            context.ReportDependency(this, pair.Names[tableLength], version?.Value, DependencyType.RustCrate, TomlUtilities.CreateLocation(context, tree, pair.Parts[tableLength]), versionLocation);
        }
    }

    /// <summary>Gets how many names of <paramref name="names"/> make the table of dependencies they are in, or 0 when they are in none.</summary>
    /// <remarks>
    /// The tables are <c>dependencies</c>, <c>dev-dependencies</c> and <c>build-dependencies</c>, the same under
    /// <c>target.'cfg(...)'</c>, and <c>workspace.dependencies</c>.
    /// </remarks>
    private static int GetDependencyTableLength(IReadOnlyList<string> names)
    {
        if (names.Count > 1 && DependencySections.Contains(names[0], StringComparer.Ordinal))
            return 1;

        if (names.Count > 2 && names[0] is "workspace" && names[1] is "dependencies")
            return 2;

        if (names.Count > 3 && names[0] is "target" && DependencySections.Contains(names[2], StringComparer.Ordinal))
            return 3;

        return 0;
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
