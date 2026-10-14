using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Cargo lockfiles (<c>Cargo.lock</c>) for the resolved versions of Rust crate dependencies.</summary>
/// <remarks>The versions are not updatable, as the checksum of each crate would no longer match.</remarks>
public sealed class CargoLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RustCrate];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Cargo.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = TomlSyntaxTree.ParseText(text, context.FullPath);

        TomlStringSyntax? name = null;
        TomlStringSyntax? version = null;
        var hasSource = false;
        var inPackage = false;
        foreach (var entry in tree.GetRoot().Entries)
        {
            switch (entry)
            {
                case TomlTableSyntax table:
                    ReportPackage();
                    inPackage = table.IsArrayOfTables && table.Key.Names is ["package"];
                    name = null;
                    version = null;
                    hasSource = false;
                    break;

                case TomlPropertySyntax property when inPackage && TomlUtilities.IsWellFormed(property):
                    switch (TomlUtilities.GetName(property.Key))
                    {
                        case "name":
                            name ??= TomlUtilities.GetString(property.Value);
                            break;

                        case "version":
                            version ??= TomlUtilities.GetString(property.Value);
                            break;

                        case "source":
                            hasSource = true;
                            break;
                    }

                    break;
            }
        }

        ReportPackage();

        void ReportPackage()
        {
            // Packages without a source are the crates of the workspace itself, or path dependencies
            if (!inPackage || name is null || version is null || !hasSource)
                return;

            // The version is not updatable as the checksum of the package would not match anymore
            context.ReportDependency(this, name.Value, version.Value, DependencyType.RustCrate,
                TomlUtilities.CreateValueLocation(context, tree, name),
                new NonUpdatableLocation(context));
        }
    }
}
