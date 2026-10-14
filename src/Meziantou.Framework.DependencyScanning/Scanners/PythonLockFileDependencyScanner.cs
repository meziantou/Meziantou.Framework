using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Python lockfiles (<c>poetry.lock</c>, <c>uv.lock</c> and <c>Pipfile.lock</c>) for the resolved versions of PyPI packages.</summary>
/// <remarks>The versions are not updatable, as the hashes of the package files would no longer match. The local projects of the lockfiles are not reported.</remarks>
public sealed class PythonLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PyPi];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("poetry.lock", ignoreCase: false)
            || context.HasFileName("uv.lock", ignoreCase: false)
            || context.HasFileName("Pipfile.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (Path.GetFileName(context.FullPath) is "Pipfile.lock")
        {
            await ScanPipfileLockAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanTomlLockAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanTomlLockAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = TomlSyntaxTree.ParseText(text, context.FullPath);

        var inPackage = false;
        var inPackageSource = false;
        TomlStringSyntax? name = null;
        TomlStringSyntax? version = null;
        var isLocalPackage = false;
        foreach (var entry in tree.GetRoot().Entries)
        {
            if (entry is TomlTableSyntax table)
            {
                var tableName = table.Key.Names;
                if (table.IsArrayOfTables && tableName is ["package"])
                {
                    ReportLockPackage();
                    inPackage = true;
                }
                else if (!inPackage || tableName is not ["package", ..])
                {
                    // The sub-tables of a package, such as [package.source] or [package.dependencies], belong to the current package
                    ReportLockPackage();
                    inPackage = false;
                }

                inPackageSource = inPackage && !table.IsArrayOfTables && tableName is ["package", "source"];
                continue;
            }

            if (!inPackage || entry is not TomlPropertySyntax property)
                continue;

            if (inPackageSource)
            {
                // poetry.lock lists the local projects with a [package.source] table whose type is directory or file
                if (property.Key.Names is ["type"] && IsLocalPoetrySourceType(property.Value))
                {
                    isLocalPackage = true;
                }

                continue;
            }

            switch (property.Key.Names)
            {
                case ["name"]:
                    name = property.Value as TomlStringSyntax;
                    break;

                case ["version"]:
                    version = property.Value as TomlStringSyntax;
                    break;

                // uv.lock lists the local projects, such as the project itself with source = { editable = "." }
                case ["source"] when property.Value is TomlInlineTableSyntax source:
                    isLocalPackage = source.Properties.Any(sourceProperty => sourceProperty.Key.Names is ["editable" or "virtual" or "path" or "directory"]
                        || (sourceProperty.Key.Names is ["type"] && IsLocalPoetrySourceType(sourceProperty.Value)));
                    break;
            }
        }

        ReportLockPackage();

        static bool IsLocalPoetrySourceType(TomlValueSyntax value) => value is TomlStringSyntax { Value: "directory" or "file" };

        // Reports the current package, if any, and resets the state so that it is reported only once
        void ReportLockPackage()
        {
            if (!isLocalPackage && name is not null && version is not null && !name.StringToken.ContainsDiagnostics && !version.StringToken.ContainsDiagnostics)
            {
                // The version is not updatable as the hashes of the package files would not match anymore
                context.ReportDependency(this, name.Value, version.Value, DependencyType.PyPi,
                    TomlUtilities.CreateValueLocation(context, tree, name),
                    new NonUpdatableLocation(context));
            }

            name = null;
            version = null;
            isLocalPackage = false;
        }
    }

    private async ValueTask ScanPipfileLockAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not { } root)
                return;

            foreach (var sectionName in new[] { "default", "develop" })
            {
                if (!JsonNodeDocument.TryGetObject(root, sectionName, out var section))
                    continue;

                foreach (var (name, value) in JsonNodeDocument.GetProperties(section))
                {
                    if (value is not JsonObject package || !JsonNodeDocument.TryGetProperty(package, "version", out var versionNode) || !JsonNodeDocument.TryGetString(versionNode, out var version))
                        continue;

                    // Versions are exact pins, such as "==2.31.0". === is an arbitrary equality, not a version.
                    if (version.StartsWith("===", StringComparison.Ordinal))
                        continue;

                    if (version.StartsWith("==", StringComparison.Ordinal))
                    {
                        version = version[2..];
                    }

                    context.ReportDependency(this, name, version, DependencyType.PyPi,
                        new NonUpdatableLocation(context), new NonUpdatableLocation(context));
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
