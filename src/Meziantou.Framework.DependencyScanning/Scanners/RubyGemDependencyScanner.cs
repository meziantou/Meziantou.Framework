using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Ruby Gemfiles, gemspecs, and Bundler lockfiles for RubyGems dependencies.</summary>
public sealed partial class RubyGemDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RubyGem];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Gemfile", ignoreCase: false)
            || context.HasFileName("Gemfile.lock", ignoreCase: false)
            || context.HasExtension(".gemspec", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var fileName = Path.GetFileName(context.FullPath);
        if (string.Equals(fileName, "Gemfile.lock", StringComparison.Ordinal))
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
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            if (line.AsSpan().TrimStart().StartsWith('#'))
                continue;

            var match = GemDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["nameDouble"].Success ? match.Groups["nameDouble"] : match.Groups["nameSingle"];
            var version = match.Groups["versionDouble"].Success ? match.Groups["versionDouble"] : match.Groups["versionSingle"];
            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length);
            var versionLocation = version.Success
                ? new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length)
                : null;

            context.ReportDependency(this, name.Value, version.Success ? version.Value : null, DependencyType.RubyGem,
                nameLocation, versionLocation);
        }
    }

    private async ValueTask ScanLockFileAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var match = LockDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"];
            var version = match.Groups["version"];
            context.ReportDependency(this, name.Value, version.Value, DependencyType.RubyGem,
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
        }
    }

    [GeneratedRegex("""(?:\bgem\b|\badd_(?:(?:runtime|development)_)?dependency\b)\s*(?:\(\s*)?(?:"(?<nameDouble>[A-Za-z0-9][A-Za-z0-9_.-]*)"|'(?<nameSingle>[A-Za-z0-9][A-Za-z0-9_.-]*)')(?:\s*,\s*(?:"(?<versionDouble>[^"]+)"|'(?<versionSingle>[^']+)'))?""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GemDependencyRegex();

    [GeneratedRegex("""^ {4}(?<name>[A-Za-z0-9][A-Za-z0-9_.-]*) \((?<version>[^)]+)\)$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockDependencyRegex();
}
