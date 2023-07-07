using System.Diagnostics;
using System.Text.RegularExpressions;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class RegexScanner : DependencyScanner
{
    private const string NameGroupName = "name";
    private const string VersionGroupName = "version";

    public string? RegexPattern { get; set; }

    public DependencyType DependencyType { get; set; }

    public GlobCollection? FilePatterns { get; set; }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (RegexPattern == null)
            return;

        using var sr = new StreamReader(context.Content);
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (Match match in Regex.Matches(text, RegexPattern, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)))
        {
            Debug.Assert(match.Success);

            var nameGroup = match.Groups[NameGroupName];
            var name = nameGroup.Value;
            if (!string.IsNullOrEmpty(name))
            {
                var versionGroup = match.Groups[VersionGroupName];
                if (versionGroup.Success)
                {
                    var version = versionGroup.Value;
                    var nameLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, nameGroup.Index, nameGroup.Length);
                    var versionLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, versionGroup.Index, versionGroup.Length);
                    context.ReportDependency(new Dependency(name, version, DependencyType, nameLocation, versionLocation));
                }
                else
                {
                    var nameLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, nameGroup.Index, nameGroup.Length);
                    context.ReportDependency(new Dependency(name, version: null, DependencyType, nameLocation, versionLocation: null));
                }                
            }
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        if (FilePatterns != null)
            return FilePatterns.IsMatch(context.Directory, context.FileName);

        return true;
    }
}
