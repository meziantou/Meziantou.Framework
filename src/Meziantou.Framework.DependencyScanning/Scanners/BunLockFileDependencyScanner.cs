using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Bun text lockfiles (<c>bun.lock</c>) for the npm packages installed from a registry.</summary>
/// <remarks>
/// The versions are not updatable, as the integrity of each package would no longer match. A package installed under another name is
/// reported as the installed package, with the install folder name in <see cref="Dependency.Metadata"/> under the <c>alias</c> key.
/// The workspaces, the packages installed from a local folder, a Git repository or a tarball URL, which have no version in the lockfile,
/// and the binary lockfile <c>bun.lockb</c> are not scanned.
/// </remarks>
public sealed class BunLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("bun.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            // bun.lock is JSON with trailing commas: "packages": { "a/@scope/b": ["@scope/b@1.0.0", "", { ... }, "sha512-..."] }
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root || !JsonNodeDocument.TryGetObject(root, "packages", out var packages))
                return;

            foreach (var (key, value) in packages)
            {
                if (value is not JsonArray { Count: > 0 } entry || !JsonNodeDocument.TryGetString(entry[0], out var resolution))
                    continue;

                // The registry packages are name@version; the others use a protocol, such as workspace:, file:, github: or https:
                if (!NpmPackageSpecifier.TrySplit(resolution, out var name, out var version) || version.Length is 0 || version.Contains(':', StringComparison.Ordinal))
                    continue;

                NpmPackageSpecifier.ReportLockedPackage(this, context, name, version, NpmPackageSpecifier.GetFolderName(key));
            }
        }
        catch (JsonException)
        {
        }
    }
}
