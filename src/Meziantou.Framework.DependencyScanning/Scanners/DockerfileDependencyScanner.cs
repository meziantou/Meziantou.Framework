using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Dockerfile and Containerfile for Docker image dependencies in FROM instructions.</summary>
public sealed partial class DockerfileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.DockerImage];

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        string? line;
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNo++;
            var match = FromRegex().Match(line);
            if (!match.Success)
                continue;

            var packageNameGroup = match.Groups["ImageName"];
            var packageName = packageNameGroup.Value;
            var versionGroup = match.Groups["Version"];
            var version = versionGroup.Value;
            context.ReportDependency(this, packageName, version, DependencyType.DockerImage,
                nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, packageNameGroup.Index + 1, packageNameGroup.Length),
                versionLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, versionGroup.Index + 1, versionGroup.Length));
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Dockerfile", ignoreCase: true);
    }

    [GeneratedRegex(@"^FROM\s*(?<ImageName>[^\s]+):(?<Version>[^\s]+)(\s+AS\s+\w+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FromRegex();
}
