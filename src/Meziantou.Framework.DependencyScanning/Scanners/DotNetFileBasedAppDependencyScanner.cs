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

            // Skip leading whitespace and comments. offset is the start of the remaining text in the line.
            var offset = 0;
            var hasCommentOnLine = false;
            while (true)
            {
                if (isInsideMultilineComment)
                {
                    var commentEndIndex = line.IndexOf("*/", offset, StringComparison.Ordinal);
                    if (commentEndIndex < 0)
                    {
                        offset = line.Length;
                        break;
                    }

                    offset = commentEndIndex + 2;
                    isInsideMultilineComment = false;
                    hasCommentOnLine = true;
                }

                while (offset < line.Length && char.IsWhiteSpace(line[offset]))
                {
                    offset++;
                }

                if (!line.AsSpan(offset).StartsWith("/*", StringComparison.Ordinal))
                    break;

                offset += 2;
                isInsideMultilineComment = true;
                hasCommentOnLine = true;
            }

            var remainingLine = line.AsSpan(offset);

            // Skip empty lines, comments, and shebang lines at the top of the file
            if (remainingLine.IsWhiteSpace() || remainingLine.StartsWith("//", StringComparison.Ordinal) || remainingLine.StartsWith("#!", StringComparison.Ordinal))
                continue;

            // Stop scanning at first non-directive line.
            // Like any preprocessor directive, a directive must be the first non-whitespace text of its line, so a directive after a comment is ignored by the compiler.
            if (!remainingLine.StartsWith("#:", StringComparison.Ordinal) || hasCommentOnLine)
                break;

            // The regexes match the whole line, so the reported columns are columns in the raw line
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
                ReportPathDependency(context, match, lineNo, DependencyType.MSBuildProjectReference);
                continue;
            }

            // Reference directive: #:ref Path
            match = RefRegex().Match(line);
            if (match.Success)
            {
                ReportPathDependency(context, match, lineNo, DependencyType.DotNetAssemblyReference);
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

    private void ReportPathDependency(ScanFileContext context, Match match, int lineNo, DependencyType type)
    {
        var pathGroup = match.Groups["Path"];
        context.ReportDependency(this, pathGroup.Value, version: null,
            type,
            nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, pathGroup.Index + 1, pathGroup.Length),
            versionLocation: null);
    }

    [GeneratedRegex(@"^\s*#:package\s+(?<Name>[^\s@]+)(?:@(?<Version>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"^\s*#:sdk\s+(?<Name>[^\s@]+)(?:@(?<Version>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex SdkRegex();

    [GeneratedRegex(@"^\s*#:project\s+(?<Path>\S(?:.*\S)?)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"^\s*#:ref\s+(?<Path>\S(?:.*\S)?)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex RefRegex();

    [GeneratedRegex(@"^\s*#:property\s+(?i:TargetFramework)\s*=\s*(?<Value>\S+)\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex TargetFrameworkPropertyRegex();
}
