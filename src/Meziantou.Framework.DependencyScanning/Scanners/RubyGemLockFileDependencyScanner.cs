using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Bundler lockfiles (<c>Gemfile.lock</c>) for the resolved versions of RubyGems dependencies.</summary>
/// <remarks>
/// The versions are not updatable. The platform of an entry, such as <c>x86_64-linux</c> in <c>nokogiri (1.15.0-x86_64-linux)</c>,
/// is available in <see cref="Dependency.Metadata"/> under the <c>platform</c> key.
/// </remarks>
public sealed partial class RubyGemLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RubyGem];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Gemfile.lock", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
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

            // The version is not updatable as Bundler resolves it from the Gemfile and may check its checksum.
            // A gem with native extensions has one entry per platform, which Bundler writes as version-platform.
            var name = match.Groups["name"];
            var platform = match.Groups["platform"];
            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length);
            if (platform.Success)
            {
                context.ReportDependency(this, name.Value, match.Groups["version"].Value, DependencyType.RubyGem, nameLocation, new NonUpdatableLocation(context),
                    tags: [], metadata: [KeyValuePair.Create<string, object?>("platform", platform.Value)]);
            }
            else
            {
                context.ReportDependency(this, name.Value, match.Groups["version"].Value, DependencyType.RubyGem, nameLocation, new NonUpdatableLocation(context));
            }
        }
    }

    // Bundler splits the version and the platform on the first '-'
    [GeneratedRegex("""^ {4}(?<name>[A-Za-z0-9][A-Za-z0-9_.-]*) \((?<version>[^-)]+)(?:-(?<platform>[^)]+))?\)$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockDependencyRegex();
}
