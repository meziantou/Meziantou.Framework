using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class PythonRequirementsDependencyScanner : DependencyScanner
{
    private static readonly Regex s_pypiReferenceRegex = new(@"^(?<PACKAGENAME>[\w\.-]+?)\s?(\[.*\])?\s?==\s?(?<VERSION>[\w\.-]*?)$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(2));

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.FileName.Equals("requirements.txt", StringComparison.Ordinal);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        string? line;
        while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            lineNo++;

            var match = s_pypiReferenceRegex.Match(line);
            if (!match.Success)
                continue;

            // Name==1.2.2
            var packageName = match.Groups["PACKAGENAME"].Value;
            var versionGroup = match.Groups["VERSION"];
            var version = versionGroup.Value;

            var column = versionGroup.Index + 1;
            await context.ReportDependency(new Dependency(packageName, version, DependencyType.PyPi, new TextLocation(context.FullPath, lineNo, column, versionGroup.Length))).ConfigureAwait(false);
        }
    }
}
