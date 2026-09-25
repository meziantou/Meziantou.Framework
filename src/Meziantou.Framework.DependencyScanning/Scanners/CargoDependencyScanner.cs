using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Cargo manifests and lockfiles for Rust crate dependencies.</summary>
public sealed class CargoDependencyScanner : DependencyScanner
{
    // dev_dependencies and build_dependencies are the deprecated spellings Cargo still accepts
    private static readonly string[] DependencyKinds =
    [
        "dependencies",
        "dev-dependencies",
        "build-dependencies",
        "dev_dependencies",
        "build_dependencies",
    ];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RustCrate];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Cargo.toml", ignoreCase: false)
            || context.HasFileName("Cargo.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tree = TomlSyntaxTree.ParseText(text, context.FullPath);
        if (Path.GetFileName(context.FullPath).Equals("Cargo.lock", StringComparison.Ordinal))
        {
            ScanLockFile(context, tree);
        }
        else
        {
            ScanManifest(context, tree);
        }
    }

    private void ScanManifest(ScanFileContext context, TomlSyntaxTree tree)
    {
        // A crate can be declared over several entries, such as a [dependencies.serde] table, or dotted keys
        // (serde.version = "1.0" and serde.features = ["derive"]), so the entries are grouped by crate before reporting.
        var crates = new Dictionary<(string Section, string Key), CrateDeclaration>();
        var orderedCrates = new List<CrateDeclaration>();

        // The key parts of the current table header, or null when its properties cannot be dependencies, such as in an
        // array of tables or after a malformed header
        IReadOnlyList<SyntaxToken>? tableParts = [];
        foreach (var entry in tree.GetRoot().Entries)
        {
            switch (entry)
            {
                case TomlTableSyntax table:
                    tableParts = table.IsArrayOfTables || table.Key.ContainsDiagnostics ? null : table.Key.Parts;
                    if (tableParts is not null)
                    {
                        // [dependencies.serde] declares the crate even when no property follows
                        Visit(tableParts, value: null);
                    }

                    break;

                case TomlPropertySyntax property when tableParts is not null && TomlUtilities.IsWellFormed(property):
                    Visit([.. tableParts, .. property.Key.Parts], property.Value);
                    break;
            }
        }

        foreach (var crate in orderedCrates)
        {
            Location versionLocation = crate.Version is not null ? TomlUtilities.CreateValueLocation(context, tree, crate.Version) : new NonUpdatableLocation(context);
            if (crate.Package is not null)
            {
                // json = { package = "serde_json" } is the serde_json crate, used as json in the code. The key is only
                // a local name, so the name of the dependency, and what to update to use another crate, is the package.
                context.ReportDependency(this, crate.Package.Value, crate.Version?.Value, DependencyType.RustCrate,
                    TomlUtilities.CreateValueLocation(context, tree, crate.Package), versionLocation,
                    tags: [],
                    metadata: [KeyValuePair.Create<string, object?>("alias", crate.Key)]);
            }
            else
            {
                // A key is renamed everywhere it appears, so it can only be updated when it appears once
                var nameLocation = crate.KeyParts.Count is 1 && !crate.HasInvalidPackage ? TomlUtilities.CreateKeyLocation(context, tree, crate.KeyParts[0]) : new NonUpdatableLocation(context);
                context.ReportDependency(this, crate.Key, crate.Version?.Value, DependencyType.RustCrate, nameLocation, versionLocation);
            }
        }

        void Visit(IReadOnlyList<SyntaxToken> parts, TomlValueSyntax? value)
        {
            var sectionLength = GetDependencySectionLength(parts);
            if (sectionLength is 0)
                return;

            if (parts.Count > sectionLength)
            {
                var crate = GetCrate(parts, sectionLength);
                switch (parts.Count - sectionLength - 1)
                {
                    case 0 when value is TomlStringSyntax:
                        // serde = "1.0"
                        crate.Version ??= TomlUtilities.GetString(value);
                        break;

                    case 1 when value is not null && parts[^1].ValueText is "version":
                        // serde.version = "1.0", or version = "1.0" in [dependencies.serde] or in serde = { ... }.
                        // Other fields, such as workspace, path or features, are not versions.
                        crate.Version ??= TomlUtilities.GetString(value);
                        break;

                    case 1 when value is not null && parts[^1].ValueText is "package":
                        if (TomlUtilities.GetString(value) is { } package)
                        {
                            crate.Package ??= package;
                        }
                        else
                        {
                            crate.HasInvalidPackage = true;
                        }

                        break;
                }
            }

            // dependencies = { serde = "1.0" } and serde = { version = "1.0" } continue the key in an inline table
            if (value is TomlInlineTableSyntax inlineTable && parts.Count <= sectionLength + 1)
            {
                foreach (var property in inlineTable.Properties)
                {
                    if (TomlUtilities.IsWellFormed(property))
                    {
                        Visit([.. parts, .. property.Key.Parts], property.Value);
                    }
                }
            }
        }

        CrateDeclaration GetCrate(IReadOnlyList<SyntaxToken> parts, int sectionLength)
        {
            var section = string.Join('\n', parts.Take(sectionLength).Select(part => part.ValueText));
            var keyPart = parts[sectionLength];
            if (!crates.TryGetValue((section, keyPart.ValueText), out var crate))
            {
                crate = new CrateDeclaration(keyPart.ValueText);
                crates.Add((section, keyPart.ValueText), crate);
                orderedCrates.Add(crate);
            }

            if (!crate.KeyParts.Exists(part => part.Span.Start == keyPart.Span.Start))
            {
                crate.KeyParts.Add(keyPart);
            }

            return crate;
        }
    }

    /// <summary>Gets the number of parts of a key that name a dependency section, or 0 when the key is not in one.</summary>
    /// <remarks>
    /// The sections are <c>[dependencies]</c> and its dev and build variants, <c>[workspace.dependencies]</c>, and the
    /// platform-specific <c>[target.'cfg(unix)'.dependencies]</c> or <c>[target.x86_64-pc-windows-gnu.dev-dependencies]</c>.
    /// </remarks>
    private static int GetDependencySectionLength(IReadOnlyList<SyntaxToken> parts)
    {
        if (parts.Count >= 1 && DependencyKinds.Contains(parts[0].ValueText, StringComparer.Ordinal))
            return 1;

        if (parts.Count >= 2 && parts[0].ValueText is "workspace" && parts[1].ValueText is "dependencies")
            return 2;

        if (parts.Count >= 3 && parts[0].ValueText is "target" && DependencyKinds.Contains(parts[2].ValueText, StringComparer.Ordinal))
            return 3;

        return 0;
    }

    private void ScanLockFile(ScanFileContext context, TomlSyntaxTree tree)
    {
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

    private sealed class CrateDeclaration(string key)
    {
        public string Key { get; } = key;

        /// <summary>Gets every occurrence of the key, such as both <c>serde</c> in <c>serde.version</c> and <c>serde.features</c>.</summary>
        public List<SyntaxToken> KeyParts { get; } = [];

        public TomlStringSyntax? Version { get; set; }

        public TomlStringSyntax? Package { get; set; }

        public bool HasInvalidPackage { get; set; }
    }
}
