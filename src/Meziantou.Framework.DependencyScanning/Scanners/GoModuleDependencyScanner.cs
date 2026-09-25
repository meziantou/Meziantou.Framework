using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Go module manifests and checksum files for Go module dependencies.</summary>
public sealed partial class GoModuleDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GoModule];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("go.mod", ignoreCase: false)
            || context.HasFileName("go.sum", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (Path.GetFileName(context.FullPath).Equals("go.sum", StringComparison.Ordinal))
        {
            await ScanSumFileAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanModFileAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanModFileAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;

        // The directive of the block the current line is in, such as require in "require (", or null outside of a block
        string? block = null;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;

            // Module paths and versions never contain "//", so a comment starts at the first one
            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            var content = commentIndex < 0 ? line : line[..commentIndex];
            var trimmed = content.AsSpan().Trim();
            if (trimmed.IsEmpty)
                continue;

            if (block is not null)
            {
                if (trimmed[0] is ')')
                {
                    block = null;
                    continue;
                }

                ScanDirective(context, block, content, content.Length - content.AsSpan().TrimStart().Length, lineNumber);
                continue;
            }

            // The verb ends at a space or at the parenthesis of a block, as in "require(" or "require ("
            var verbLength = trimmed.IndexOfAny(' ', '\t', '(');
            if (verbLength <= 0)
                continue;

            var verb = trimmed[..verbLength].ToString();
            var arguments = trimmed[verbLength..].TrimStart();
            if (arguments is ['(', ..])
            {
                // "require ()" is an empty block
                if (!arguments[1..].TrimStart().StartsWith(')'))
                {
                    block = verb;
                }

                continue;
            }

            ScanDirective(context, verb, content, content.AsSpan().TrimEnd().Length - arguments.Length, lineNumber);
        }
    }

    /// <summary>Scans the arguments of a directive, which start at <paramref name="argumentsStart"/> in <paramref name="content"/>.</summary>
    private void ScanDirective(ScanFileContext context, string verb, string content, int argumentsStart, int lineNumber)
    {
        var arguments = content[argumentsStart..];
        switch (verb)
        {
            case "require":
                {
                    var match = RequireRegex().Match(arguments);
                    if (!match.Success)
                        return;

                    var name = match.Groups["name"];
                    var version = match.Groups["version"];
                    context.ReportDependency(this, name.Value, version.Value, DependencyType.GoModule,
                        CreateLocation(name),
                        CreateLocation(version));
                    break;
                }

            case "replace":
                {
                    // replace example.com/a => example.com/fork v1.2.3 reports the replacement. A replacement without a
                    // version is a local directory, such as ../a, which is not a dependency.
                    var match = ReplaceRegex().Match(arguments);
                    if (!match.Success || !match.Groups["version"].Success)
                        return;

                    var name = match.Groups["name"];
                    var version = match.Groups["version"];
                    context.ReportDependency(this, name.Value, version.Value, DependencyType.GoModule,
                        CreateLocation(name),
                        CreateLocation(version),
                        tags: [],
                        metadata: [KeyValuePair.Create<string, object?>("replaces", match.Groups["old"].Value)]);
                    break;
                }

            case "go" or "toolchain":
                {
                    // The go command handles both as modules, as in "go get go@1.23.0 toolchain@go1.23.0"
                    var match = (verb is "go" ? GoVersionRegex() : ToolchainRegex()).Match(arguments);
                    if (!match.Success)
                        return;

                    context.ReportDependency(this, verb, match.Groups["version"].Value, DependencyType.GoModule,
                        new NonUpdatableLocation(context),
                        CreateLocation(match.Groups["version"]));
                    break;
                }
        }

        TextLocation CreateLocation(Group group) => new(context.FileSystem, context.FullPath, lineNumber, argumentsStart + group.Index + 1, group.Length);
    }

    private async ValueTask ScanSumFileAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var match = SumDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            // The version is not updatable as the h1: hash would not match anymore
            var name = match.Groups["name"];
            context.ReportDependency(this, name.Value, match.Groups["version"].Value, DependencyType.GoModule,
                new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length),
                new NonUpdatableLocation(context));
        }
    }

    [GeneratedRegex("""^(?<name>[^\s"`]+)\s+(?<version>v\S+)\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex RequireRegex();

    [GeneratedRegex("""^(?<old>[^\s"`]+)(?:\s+v\S+)?\s+=>\s+(?<name>[^\s"`]+)(?:\s+(?<version>v\S+))?\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ReplaceRegex();

    [GeneratedRegex("""^(?<version>[0-9]\S*)\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GoVersionRegex();

    [GeneratedRegex("""^(?<version>go[0-9]\S*)\s*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ToolchainRegex();

    [GeneratedRegex("""^(?<name>\S+)\s+(?<version>v[^\s/]+)(?:/go\.mod)?\s+h1:[^\s]+$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SumDependencyRegex();
}
