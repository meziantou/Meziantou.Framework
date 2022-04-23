using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class RegexScanner : DependencyScanner
{
    private const string NameGroupName = "name";
    private const string VersionGroupName = "version";

    public string? RegexPattern { get; set; }

    public DependencyType DependencyType { get; set; }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (RegexPattern == null)
            return;

        using var sr = new StreamReader(context.Content);
#if NET7_0_OR_GREATER
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
#else
        var text = await sr.ReadToEndAsync().ConfigureAwait(false);
#endif

        foreach (Match match in Regex.Matches(text, RegexPattern, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)))
        {
            Debug.Assert(match.Success);

            var name = match.Groups[NameGroupName].Value;
            var versionGroup = match.Groups[VersionGroupName];
            var version = versionGroup.Value;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
            {
                var location = TextLocation.FromIndex(context.FullPath, text, versionGroup.Index, versionGroup.Length);
                await context.ReportDependency(new Dependency(name, version, DependencyType, location)).ConfigureAwait(false);
            }
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // The behavior should be handled by the base class with the FilePatterns property
        return false;
    }
}
