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

            if (!isDependencySection || entry is not TomlPropertySyntax property)
                continue;

            var versionText = property.Value.Trim('"');
            var version = versionText.StartsWith('{', StringComparison.Ordinal) ? ExtractVersion(versionText) : versionText;
            var nameLocation = CreateLocation(context, tree, property.KeyToken.Span);
            Location? versionLocation = version is not null
                ? CreateVersionLocation(context, tree, property.ValueNode.AsToken().Span, property.Value, version)
                : new NonUpdatableLocation(context);
            context.ReportDependency(this, property.Key, version, DependencyType.RustCrate, nameLocation, versionLocation);
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

            var version = versionMatch.Groups["version"];
            context.ReportDependency(this, packageName, version.Value, DependencyType.RustCrate,
                new TextLocation(context.FileSystem, context.FullPath, packageNameLine, packageNameColumn, packageName.Length),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
            packageName = null;
        }
    }

    private static string? ExtractVersion(string value)
    {
        const string Prefix = "version";
        var index = value.IndexOf(Prefix, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var quoteStart = value.IndexOf('"', index + Prefix.Length, StringComparison.Ordinal);
        var quoteEnd = quoteStart >= 0 ? value.IndexOf('"', quoteStart + 1, StringComparison.Ordinal) : -1;
        return quoteStart >= 0 && quoteEnd > quoteStart ? value[(quoteStart + 1)..quoteEnd] : null;
    }

    private static TextLocation CreateLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan span)
    {
        var lineSpan = tree.GetLineSpan(span);
        return new TextLocation(context.FileSystem, context.FullPath, lineSpan.Start.Line + 1, lineSpan.Start.Character + 1, span.Length);
    }

    private static TextLocation CreateVersionLocation(ScanFileContext context, TomlSyntaxTree tree, TextSpan valueSpan, string value, string version)
    {
        var versionOffset = value.StartsWith('"', StringComparison.Ordinal)
            ? 1
            : value.IndexOf('"', StringComparison.Ordinal) + 1;
        return CreateLocation(context, tree, new TextSpan(valueSpan.Start + versionOffset, version.Length));
    }

    [GeneratedRegex("""^\s*name\s*=\s*"(?<name>[^"]+)"\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockPackageNameRegex();

    [GeneratedRegex("""^\s*version\s*=\s*"(?<version>[^"]+)"\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockPackageVersionRegex();
}
