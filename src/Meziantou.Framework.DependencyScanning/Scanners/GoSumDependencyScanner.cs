using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Go checksum files (<c>go.sum</c>) for Go module dependencies.</summary>
/// <remarks>The versions are not updatable, as the hash of each module would no longer match.</remarks>
public sealed partial class GoSumDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GoModule];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("go.sum", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var match = SumDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            // The version is not updatable as the h1: hash would not match anymore
            var name = match.Groups["name"];
            context.ReportDependency(this, name.Value, match.Groups["version"].Value, DependencyType.GoModule,
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length),
                new NonUpdatableLocation(context));
        }
    }

    [GeneratedRegex("""^(?<name>\S+)\s+(?<version>v[^\s/]+)(?:/go\.mod)?\s+h1:[^\s]+$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SumDependencyRegex();
}
