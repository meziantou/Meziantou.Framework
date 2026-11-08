using System.Globalization;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans pnpm lockfiles (<c>pnpm-lock.yaml</c>) for the installed npm packages.</summary>
/// <remarks>
/// The entries of <c>packages</c> are reported once per name and version, as pnpm writes one entry per set of peer dependencies in
/// lockfile versions 5 and 6. Lockfile versions 5 (<c>/name/1.0.0_peer@2.0.0</c>), 6 (<c>/name@1.0.0(peer@2.0.0)</c>) and 9 (<c>name@1.0.0</c>) are supported.
/// The versions are not updatable, as the integrity of each package would no longer match. The packages installed from a local folder are not reported.
/// </remarks>
public sealed class PnpmLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("pnpm-lock.yaml", ignoreCase: false);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlFile(context);
        if (yaml is null)
            return ValueTask.CompletedTask;

        // pnpm 10 writes the packages of configDependencies and packageManager in a first document
        var reported = new HashSet<(string Name, string Version)>();
        foreach (var document in yaml.Stream)
        {
            if (document.Contents is not YamlMapping root || GetProperty(root, "packages", StringComparison.Ordinal) is not YamlMapping packages)
                continue;

            var usesSlashSeparator = GetMajorVersion(GetScalarValue(GetProperty(root, "lockfileVersion", StringComparison.Ordinal))) is < 6;
            foreach (var (keyNode, value) in packages)
            {
                if (GetScalarValue(keyNode) is not { } key || !TryGetPackage(key, value, usesSlashSeparator, out var name, out var version))
                    continue;

                if (reported.Add((name, version)))
                {
                    NpmPackageSpecifier.ReportLockedPackage(this, context, name, version, alias: null);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool TryGetPackage(string key, YamlElement? entry, bool usesSlashSeparator, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out string? version)
    {
        name = null;
        version = null;

        // A local folder is not installed from a registry
        if (GetProperty(GetProperty(entry, "resolution", StringComparison.Ordinal), "directory", StringComparison.Ordinal) is not null)
            return false;

        // The packages that are not from a registry, such as Git repositories and tarballs, write their name and version
        var nameField = GetScalarValue(GetProperty(entry, "name", StringComparison.Ordinal));
        var versionField = GetScalarValue(GetProperty(entry, "version", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(nameField) && !string.IsNullOrEmpty(versionField))
        {
            name = nameField;
            version = versionField;
        }
        else if (usesSlashSeparator)
        {
            // /name/1.0.0_peer@2.0.0 and /@scope/name/1.0.0
            var index = key.LastIndexOf('/', StringComparison.Ordinal);
            if (key is not ['/', ..] || index <= 1)
                return false;

            name = key[1..index];
            version = TrimSuffix(key[(index + 1)..], '_');
        }
        else
        {
            // /name@1.0.0(peer@2.0.0) in lockfile version 6, name@1.0.0(peer@2.0.0) in lockfile version 9
            if (!NpmPackageSpecifier.TrySplit(key is ['/', ..] ? key[1..] : key, out name, out var specifier))
                return false;

            version = string.IsNullOrEmpty(versionField) ? TrimSuffix(specifier, '(') : versionField;
        }

        // link:, file: and URLs are not versions
        return name.Length > 0 && version.Length > 0 && !version.Contains(':', StringComparison.Ordinal);
    }

    private static string TrimSuffix(string value, char separator)
    {
        var index = value.IndexOf(separator, StringComparison.Ordinal);
        return index < 0 ? value : value[..index];
    }

    private static int? GetMajorVersion(string? lockfileVersion)
    {
        if (lockfileVersion is null)
            return null;

        var index = lockfileVersion.IndexOf('.', StringComparison.Ordinal);
        return int.TryParse(index < 0 ? lockfileVersion : lockfileVersion[..index], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
    }
}
