using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class DockerfileDependencyScanner : DependencyScanner
{
    private static readonly Regex s_fromRegex = new(@"^FROM\s*(?<ImageName>[^\s]+):(?<Version>[^\s]+)(\s+AS\s+\w+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        string? line;
        while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            lineNo++;
            var match = s_fromRegex.Match(line);
            if (!match.Success)
                continue;

            var packageName = match.Groups["ImageName"].Value;
            var versionGroup = match.Groups["Version"];
            var version = versionGroup.Value;
            var column = versionGroup.Index + 1;
            await context.ReportDependency(new Dependency(packageName, version, DependencyType.DockerImage, new TextLocation(context.FullPath, lineNo, column, versionGroup.Length))).ConfigureAwait(false);
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.FileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase);
    }
}
