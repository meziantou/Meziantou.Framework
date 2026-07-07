using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans .NET file-based app (.cs) files for package, SDK, project, reference, and target framework directives.</summary>
public sealed partial class DotNetFileBasedAppDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } =
    [
        DependencyType.NuGet,
        DependencyType.MSBuildProjectReference,
        DependencyType.DotNetAssemblyReference,
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
        var isInsideMultilineComment = false;
        string? line;
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNo++;

            var remainingLine = line;
            while (true)
            {
                if (isInsideMultilineComment)
                {
                    var commentEndIndex = remainingLine.IndexOf("*/", StringComparison.Ordinal);
                    if (commentEndIndex < 0)
                    {
                        remainingLine = "";
                        break;
                    }

                    remainingLine = remainingLine[(commentEndIndex + 2)..];
                    isInsideMultilineComment = false;
                }

                remainingLine = remainingLine.TrimStart();
                if (!remainingLine.StartsWith("/*", StringComparison.Ordinal))
                    break;

                remainingLine = remainingLine[2..];
                isInsideMultilineComment = true;
            }

            // Skip empty lines, comments, and shebang lines at the top of the file
            if (string.IsNullOrWhiteSpace(remainingLine) || remainingLine.StartsWith("//", StringComparison.Ordinal) || remainingLine.StartsWith("#!", StringComparison.Ordinal))
                continue;

            // Stop scanning at first non-directive line
            if (!remainingLine.StartsWith("#:", StringComparison.Ordinal))
                break;

            // Package directive: #:package Name or #:package Name@Version
            var match = PackageRegex().Match(remainingLine);
            if (match.Success)
            {
                ReportNameVersionDependency(context, match, lineNo, DependencyType.NuGet);
                continue;
            }

            // SDK directive: #:sdk Name or #:sdk Name@Version
            match = SdkRegex().Match(remainingLine);
            if (match.Success)
            {
                ReportNameVersionDependency(context, match, lineNo, DependencyType.NuGet);
                continue;
            }

            // Project directive: #:project Path
            match = ProjectRegex().Match(remainingLine);
            if (match.Success)
            {
                ReportPathDependency(context, match, lineNo, DependencyType.MSBuildProjectReference);
                continue;
            }

            // Reference directive: #:ref Path
            match = RefRegex().Match(remainingLine);
            if (match.Success)
            {
                ReportPathDependency(context, match, lineNo, DependencyType.DotNetAssemblyReference);
                continue;
            }

            // Property directive: #:property TargetFramework=value
            match = TargetFrameworkPropertyRegex().Match(remainingLine);
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

    private void ReportPathDependency(ScanFileContext context, Match match, int lineNo, DependencyType type)
    {
        var pathGroup = match.Groups["Path"];
        context.ReportDependency(this, pathGroup.Value, version: null,
            type,
            nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, pathGroup.Index + 1, pathGroup.Length),
            versionLocation: null);
    }

    [GeneratedRegex(@"^#:package\s+(?<Name>[^\s@]+)(?:@(?<Version>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"^#:sdk\s+(?<Name>[^\s@]+)(?:@(?<Version>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex SdkRegex();

    [GeneratedRegex(@"^#:project\s+(?<Path>\S+)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"^#:ref\s+(?<Path>\S+)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex RefRegex();

    [GeneratedRegex(@"^#:property\s+TargetFramework=(?<Value>\S+)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex TargetFrameworkPropertyRegex();
}
