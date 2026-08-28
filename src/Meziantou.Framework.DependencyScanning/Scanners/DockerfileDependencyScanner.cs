using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Dockerfile and Containerfile for Docker image dependencies in FROM and COPY --from instructions.</summary>
public sealed partial class DockerfileDependencyScanner : DependencyScanner
{
    private static readonly string[] FileNames = ["Dockerfile", "Containerfile"];
    private static readonly string[] Extensions = [".Dockerfile", ".Containerfile"];

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
            {
                match = CopyFromRegex().Match(line);
                if (!match.Success)
                    continue;
            }

            var packageNameGroup = match.Groups["ImageName"];
            var packageName = packageNameGroup.Value;
            if (packageName.Contains('@', StringComparison.Ordinal))
                continue;

            var versionGroup = match.Groups["Version"];
            var version = versionGroup.Value;
            context.ReportDependency(this, packageName, version, DependencyType.DockerImage,
                nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, packageNameGroup.Index + 1, packageNameGroup.Length),
                versionLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, versionGroup.Index + 1, versionGroup.Length));
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // <name>.Dockerfile
        if (context.HasExtension(Extensions, ignoreCase: true))
            return true;

        foreach (var fileName in FileNames)
        {
            if (context.HasFileName(fileName, ignoreCase: true))
                return true;

            // Dockerfile.<name>
            if (context.FileName.Length > fileName.Length &&
                context.FileName[fileName.Length] is '.' &&
                context.FileName.StartsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^FROM\s*(?<ImageName>[^\s]+):(?<Version>[^\s]+)(\s+AS\s+\w+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex FromRegex();

    [GeneratedRegex(@"^\s*COPY\b.*?\s--from(?:=|\s+)(?<ImageName>[^\s]+):(?<Version>[^\s]+)(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex CopyFromRegex();
}
