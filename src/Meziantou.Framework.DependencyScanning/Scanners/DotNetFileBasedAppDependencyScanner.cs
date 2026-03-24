using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans .NET file-based app (.cs) files for package, SDK, project, and target framework directives.</summary>
public sealed partial class DotNetFileBasedAppDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } =
    [
        DependencyType.NuGet,
        DependencyType.MSBuildProjectReference,
        DependencyType.DotNetTargetFramework,
    ];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasExtension(".cs", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        string? line;
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNo++;

            // Skip empty lines, comments, and shebang lines at the top of the file
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#!", StringComparison.Ordinal))
                continue;

            // Stop scanning at first non-directive line
            if (!line.StartsWith("#:", StringComparison.Ordinal))
                break;

            // Package directive: #:package Name or #:package Name@Version
            var match = PackageRegex().Match(line);
            if (match.Success)
            {
                ReportNameVersionDependency(context, match, lineNo, DependencyType.NuGet);
                continue;
            }

            // SDK directive: #:sdk Name or #:sdk Name@Version
            match = SdkRegex().Match(line);
            if (match.Success)
            {
                ReportNameVersionDependency(context, match, lineNo, DependencyType.NuGet);
                continue;
            }

            // Project directive: #:project Path
            match = ProjectRegex().Match(line);
            if (match.Success)
            {
                var pathGroup = match.Groups["Path"];
                context.ReportDependency(this, pathGroup.Value, version: null,
                    DependencyType.MSBuildProjectReference,
                    nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, pathGroup.Index + 1, pathGroup.Length),
                    versionLocation: null);
                continue;
            }

            // Property directive: #:property TargetFramework=value
            match = TargetFrameworkPropertyRegex().Match(line);
            if (match.Success)
            {
                var valueGroup = match.Groups["Value"];
                context.ReportDependency(this, name: null, valueGroup.Value,
                    DependencyType.DotNetTargetFramework,
                    nameLocation: null,
                    versionLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, valueGroup.Index + 1, valueGroup.Length));
                continue;
            }
        }
    }

    private void ReportNameVersionDependency(ScanFileContext context, Match match, int lineNo, DependencyType type)
    {
        var nameGroup = match.Groups["Name"];
        var versionGroup = match.Groups["Version"];
        context.ReportDependency(this, nameGroup.Value,
            versionGroup.Success ? versionGroup.Value : null,
            type,
            nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, nameGroup.Index + 1, nameGroup.Length),
            versionLocation: versionGroup.Success ? new TextLocation(context.FileSystem, context.FullPath, lineNo, versionGroup.Index + 1, versionGroup.Length) : null);
    }

    [GeneratedRegex(@"^#:package\s+(?<Name>[^\s@]+)(?:@(?<Version>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"^#:sdk\s+(?<Name>[^\s@]+)(?:@(?<Version>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex SdkRegex();

    [GeneratedRegex(@"^#:project\s+(?<Path>\S+)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"^#:property\s+TargetFramework=(?<Value>\S+)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex TargetFrameworkPropertyRegex();
}
