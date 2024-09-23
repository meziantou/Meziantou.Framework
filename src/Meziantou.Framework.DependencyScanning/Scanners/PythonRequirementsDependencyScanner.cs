using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class PythonRequirementsDependencyScanner : DependencyScanner
{
    private static readonly Regex PypiReferenceRegex = new(@"^(?<PACKAGENAME>[\w\.-]+?)\s?(\[.*\])?\s?==\s?(?<VERSION>[\w\.-]*?)$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(2));

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("requirements.txt", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        string? line;
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNo++;

            var match = PypiReferenceRegex.Match(line);
            if (!match.Success)
                continue;

            // Name==1.2.2
            var packageNameGroup = match.Groups["PACKAGENAME"];
            var packageName = packageNameGroup.Value;
            var versionGroup = match.Groups["VERSION"];
            var version = versionGroup.Value;

            context.ReportDependency<PythonRequirementsDependencyScanner>(packageName, version, DependencyType.PyPi,
                nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, packageNameGroup.Index + 1, packageNameGroup.Length),
                versionLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, versionGroup.Index + 1, versionGroup.Length));
        }
    }
}
