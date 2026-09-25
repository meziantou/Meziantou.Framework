using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Go module manifests and checksum files for Go module dependencies.</summary>
public sealed partial class GoModuleDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GoModule];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("go.mod", ignoreCase: false)
            || context.HasFileName("go.sum", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (Path.GetFileName(context.FullPath).Equals("go.sum", StringComparison.Ordinal))
        {
            await ScanSumFileAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanModFileAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanModFileAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        var inRequireBlock = false;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var trimmedLine = line.AsSpan().Trim();
            if (trimmedLine.StartsWith("require", StringComparison.Ordinal) && (trimmedLine.Length is 7 || char.IsWhiteSpace(trimmedLine[7]) || trimmedLine[7] is '('))
            {
                inRequireBlock = trimmedLine.StartsWith("require (", StringComparison.Ordinal);
                if (inRequireBlock)
                    continue;
            }
            else if (inRequireBlock && trimmedLine.StartsWith(')'))
            {
                inRequireBlock = false;
                continue;
            }
            else if (!inRequireBlock)
            {
                continue;
            }

            var match = ModDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"];
            var version = match.Groups["version"];
            context.ReportDependency(this, name.Value, version.Value, DependencyType.GoModule,
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
        }
    }

    private async ValueTask ScanSumFileAsync(ScanFileContext context)
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

            var name = match.Groups["name"];
            var version = match.Groups["version"];
            context.ReportDependency(this, name.Value, version.Value, DependencyType.GoModule,
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length),
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, version.Index + 1, version.Length));
        }
    }

    [GeneratedRegex("""^\s*(?:require\s+)?(?<name>[^\s]+)\s+(?<version>v[^\s]+)(?:\s+//.*)?$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ModDependencyRegex();

    [GeneratedRegex("""^(?<name>\S+)\s+(?<version>v[^\s/]+)(?:/go\.mod)?\s+h1:[^\s]+$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SumDependencyRegex();
}
