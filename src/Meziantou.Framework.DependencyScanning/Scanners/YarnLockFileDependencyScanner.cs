using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Yarn lockfiles (<c>yarn.lock</c>) for the installed npm packages, in the Yarn 1 format and in the YAML format of Yarn 2 and later.</summary>
/// <remarks>
/// The versions are not updatable, as the checksum of each package would no longer match. A package installed under another name is
/// reported as the installed package, with the name it is installed under in <see cref="Dependency.Metadata"/> under the <c>alias</c> key.
/// The workspaces, the packages installed from a local folder (<c>file:</c>, <c>link:</c>, <c>portal:</c>) and the patched copies of packages are not reported.
/// </remarks>
public sealed class YarnLockFileDependencyScanner : DependencyScanner
{
    private static readonly string[] LocalProtocols = ["workspace:", "file:", "link:", "portal:", "patch:", "exec:"];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Npm];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("yarn.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        // Yarn 2 and later write YAML with a __metadata entry; Yarn 1 writes its own format, which is not YAML
        if (text.StartsWith("__metadata:", StringComparison.Ordinal) || text.Contains("\n__metadata:", StringComparison.Ordinal))
        {
            ScanBerryLockFile(context, text);
        }
        else
        {
            ScanClassicLockFile(context, text);
        }
    }

    private void ScanBerryLockFile(ScanFileContext context, string text)
    {
        YamlStream stream;
        try
        {
            stream = YamlStream.Load(new StringReader(text));
        }
        catch
        {
            return;
        }

        foreach (var document in stream)
        {
            if (document.Contents is not YamlMapping root)
                continue;

            foreach (var (keyNode, value) in root)
            {
                // The key lists the descriptors that resolve to this entry: "alias@npm:name@^1.0.0, alias@npm:name@^1.1.0"
                if (GetScalarValue(keyNode) is not { } key || key is "__metadata")
                    continue;

                // The resolution is name@npm:1.0.0 for the registry, or another protocol such as name@https://...
                var resolution = GetScalarValue(GetProperty(value, "resolution", StringComparison.Ordinal));
                if (resolution is null || !NpmPackageSpecifier.TrySplit(resolution, out var name, out var reference) || IsLocal(reference))
                    continue;

                var version = reference.StartsWith("npm:", StringComparison.Ordinal) ? reference[4..] : GetScalarValue(GetProperty(value, "version", StringComparison.Ordinal));
                if (string.IsNullOrEmpty(version))
                    continue;

                var firstDescriptor = key.Split(',', 2)[0].Trim();
                NpmPackageSpecifier.TrySplit(firstDescriptor, out var descriptorName, out _);
                NpmPackageSpecifier.ReportLockedPackage(this, context, name, version, alias: descriptorName);
            }
        }
    }

    private void ScanClassicLockFile(ScanFileContext context, string text)
    {
        // Each entry starts with the specifiers that resolve to it, then its fields indented with two spaces:
        // "@scope/a@^1.0.0", "@scope/a@^1.1.0":
        //   version "1.1.0"
        string? name = null;
        string? alias = null;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (line.IsEmpty || line[0] is '#')
                continue;

            if (line[0] is not ' ')
            {
                name = null;
                alias = null;
                var header = line.TrimEnd();
                if (header is [.., ':'] && TryParseClassicHeader(header[..^1].ToString(), out var headerName, out var headerAlias))
                {
                    name = headerName;
                    alias = headerAlias;
                }

                continue;
            }

            if (name is not null && line.StartsWith("  version ", StringComparison.Ordinal))
            {
                var version = Unquote(line["  version ".Length..].Trim().ToString());
                if (version.Length > 0)
                {
                    NpmPackageSpecifier.ReportLockedPackage(this, context, name, version, alias);
                }

                name = null;
            }
        }
    }

    private static bool TryParseClassicHeader(string header, [NotNullWhen(true)] out string? name, out string? alias)
    {
        alias = null;
        var firstSpecifier = Unquote(header.Split(',', 2)[0].Trim());
        if (!NpmPackageSpecifier.TrySplit(firstSpecifier, out name, out var range) || IsLocal(range))
        {
            name = null;
            return false;
        }

        // A package installed under another name: alias@npm:name@^1.0.0
        if (range.StartsWith("npm:", StringComparison.Ordinal) && NpmPackageSpecifier.TrySplit(range[4..], out var aliasedName, out _))
        {
            alias = name;
            name = aliasedName;
        }

        return true;
    }

    private static bool IsLocal(string reference)
    {
        foreach (var protocol in LocalProtocols)
        {
            if (reference.StartsWith(protocol, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Unquote(string value)
    {
        return value is ['"', .., '"'] ? value[1..^1] : value;
    }
}
