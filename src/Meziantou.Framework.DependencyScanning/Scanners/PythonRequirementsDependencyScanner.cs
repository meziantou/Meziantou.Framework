using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class PythonRequirementsDependencyScanner : DependencyScanner
{
    private static readonly Regex PypiReferenceRegex = new(@"^(?<PACKAGENAME>[\w\.-]+?)\s?(\[.*\])?\s?==\s?(?<VERSION>[\w\.-]*?)$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(2));

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.FileName.Equals("requirements.txt", StringComparison.Ordinal);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        string? line;
#if NET7_0_OR_GREATER
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) != null)
#else
        while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
#endif
        {
            lineNo++;

            var match = PypiReferenceRegex.Match(line);
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
