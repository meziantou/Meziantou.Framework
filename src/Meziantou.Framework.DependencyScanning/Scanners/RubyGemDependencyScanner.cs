using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Ruby Gemfiles, gemspecs, and Bundler lockfiles for RubyGems dependencies.</summary>
/// <remarks>
/// A gem declared with several requirements, such as <c>gem "rails", "~> 7.0", "&gt;= 7.0.1"</c>, is reported with the requirements
/// joined by <c>", "</c> and a version location that is not updatable. The platform of a lockfile entry, such as <c>x86_64-linux</c>
/// in <c>nokogiri (1.15.0-x86_64-linux)</c>, is available in <see cref="Dependency.Metadata"/> under the <c>platform</c> key.
/// </remarks>
public sealed partial class RubyGemDependencyScanner : DependencyScanner
{
    private static readonly string[] AddDependencyMethodNames = ["add_dependency", "add_runtime_dependency", "add_development_dependency"];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RubyGem];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Gemfile", ignoreCase: false)
            || context.HasFileName("Gemfile.lock", ignoreCase: false)
            || context.HasExtension(".gemspec", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var fileName = Path.GetFileName(context.FullPath);
        if (string.Equals(fileName, "Gemfile.lock", StringComparison.Ordinal))
        {
            await ScanLockFileAsync(context).ConfigureAwait(false);
        }
        else
        {
            await ScanManifestAsync(context).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanManifestAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);
        var tokens = RubyLexer.Tokenize(text);
        TextLineMap? lineMap = null;
        var requirements = new List<RubyToken>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!IsDependencyDeclaration(text, tokens, i))
                continue;

            // gem "name", "requirement", ...
            // spec.add_dependency("name".freeze, ["requirement".freeze, "requirement".freeze])
            var index = i + 1;
            if (index < tokens.Count && tokens[index].Is(text, RubyTokenKind.Punctuation, "("))
            {
                index = SkipNewLines(tokens, index + 1);
            }

            if (index >= tokens.Count || tokens[index].Kind is not RubyTokenKind.String)
                continue;

            var nameToken = tokens[index];
            var name = nameToken.GetContent(text);
            if (!GemNameRegex().IsMatch(name))
                continue;

            index = SkipFreeze(text, tokens, index + 1);
            requirements.Clear();
            ReadRequirements(text, tokens, index, requirements);

            lineMap ??= new TextLineMap(text);
            var nameLocation = CreateLocation(context, lineMap, nameToken);
            switch (requirements)
            {
                case []:
                    context.ReportDependency(this, name, version: null, DependencyType.RubyGem, nameLocation, versionLocation: null);
                    break;

                case [var requirement]:
                    context.ReportDependency(this, name, requirement.GetContent(text), DependencyType.RubyGem, nameLocation, CreateLocation(context, lineMap, requirement));
                    break;

                default:
                    // Several requirements cannot be updated as a single version
                    context.ReportDependency(this, name, string.Join(", ", requirements.Select(requirement => requirement.GetContent(text))), DependencyType.RubyGem,
                        nameLocation, new NonUpdatableLocation(context));
                    break;
            }
        }
    }

    /// <summary>Whether the token is a <c>gem</c> or <c>add_*dependency</c> call that starts a statement.</summary>
    private static bool IsDependencyDeclaration(string text, List<RubyToken> tokens, int index)
    {
        var token = tokens[index];
        if (token.Kind is not RubyTokenKind.Identifier)
            return false;

        var name = text.AsSpan(token.Start, token.End - token.Start);
        if (name.SequenceEqual("gem"))
            return IsStatementStart(text, tokens, index);

        foreach (var methodName in AddDependencyMethodNames)
        {
            if (!name.SequenceEqual(methodName))
                continue;

            // spec.add_dependency, or add_dependency called on the implicit receiver
            if (index >= 2 &&
                (tokens[index - 1].Is(text, RubyTokenKind.Punctuation, ".") || tokens[index - 1].Is(text, RubyTokenKind.Punctuation, "&.")) &&
                tokens[index - 2].Kind is RubyTokenKind.Identifier or RubyTokenKind.Other)
            {
                return IsStatementStart(text, tokens, index - 2);
            }

            return IsStatementStart(text, tokens, index);
        }

        return false;
    }

    private static bool IsStatementStart(string text, List<RubyToken> tokens, int index)
    {
        if (index == 0)
            return true;

        var previous = tokens[index - 1];
        return previous.Kind is RubyTokenKind.NewLine
            || previous.Is(text, RubyTokenKind.Punctuation, ";")
            || previous.Is(text, RubyTokenKind.Punctuation, "{")
            || previous.Is(text, RubyTokenKind.Identifier, "do")
            || previous.Is(text, RubyTokenKind.Identifier, "then")
            || previous.Is(text, RubyTokenKind.Identifier, "else")
            || previous.Is(text, RubyTokenKind.Identifier, "begin");
    }

    /// <summary>Reads the requirement strings that follow the name, either as arguments or as an array.</summary>
    private static void ReadRequirements(string text, List<RubyToken> tokens, int index, List<RubyToken> requirements)
    {
        while (index < tokens.Count && tokens[index].Is(text, RubyTokenKind.Punctuation, ","))
        {
            index = SkipNewLines(tokens, index + 1);
            if (index >= tokens.Count)
                return;

            if (tokens[index].Kind is RubyTokenKind.String)
            {
                requirements.Add(tokens[index]);
                index = SkipFreeze(text, tokens, index + 1);
                continue;
            }

            if (!tokens[index].Is(text, RubyTokenKind.Punctuation, "["))
                return;

            // ["~> 1.0", ">= 1.0.2"]
            index = SkipNewLines(tokens, index + 1);
            while (index < tokens.Count && tokens[index].Kind is RubyTokenKind.String)
            {
                requirements.Add(tokens[index]);
                index = SkipNewLines(tokens, SkipFreeze(text, tokens, index + 1));
                if (index < tokens.Count && tokens[index].Is(text, RubyTokenKind.Punctuation, ","))
                {
                    index = SkipNewLines(tokens, index + 1);
                }
            }

            if (index >= tokens.Count || !tokens[index].Is(text, RubyTokenKind.Punctuation, "]"))
                return;

            index = SkipFreeze(text, tokens, index + 1);
        }
    }

    private static int SkipNewLines(List<RubyToken> tokens, int index)
    {
        while (index < tokens.Count && tokens[index].Kind is RubyTokenKind.NewLine)
        {
            index++;
        }

        return index;
    }

    /// <summary>Skips a <c>.freeze</c> call, which generated gemspecs add to every string.</summary>
    private static int SkipFreeze(string text, List<RubyToken> tokens, int index)
    {
        if (index + 1 < tokens.Count && tokens[index].Is(text, RubyTokenKind.Punctuation, ".") && tokens[index + 1].Is(text, RubyTokenKind.Identifier, "freeze"))
            return index + 2;

        return index;
    }

    private static Location CreateLocation(ScanFileContext context, TextLineMap lineMap, RubyToken token)
    {
        // A string with an escape sequence or an interpolation does not hold its value as written
        if (!token.IsVerbatim)
            return new NonUpdatableLocation(context);

        return TextLocation.FromIndex(context.FileSystem, context.FullPath, lineMap, token.ContentStart, token.ContentEnd - token.ContentStart);
    }

    private async ValueTask ScanLockFileAsync(ScanFileContext context)
    {
        using var reader = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNumber++;
            var match = LockDependencyRegex().Match(line);
            if (!match.Success)
                continue;

            // The version is not updatable as Bundler resolves it from the Gemfile and may check its checksum.
            // A gem with native extensions has one entry per platform, which Bundler writes as version-platform.
            var name = match.Groups["name"];
            var platform = match.Groups["platform"];
            var nameLocation = new TextLocation(context.FileSystem, context.FullPath, lineNumber, name.Index + 1, name.Length);
            if (platform.Success)
            {
                context.ReportDependency(this, name.Value, match.Groups["version"].Value, DependencyType.RubyGem, nameLocation, new NonUpdatableLocation(context),
                    tags: [], metadata: [KeyValuePair.Create<string, object?>("platform", platform.Value)]);
            }
            else
            {
                context.ReportDependency(this, name.Value, match.Groups["version"].Value, DependencyType.RubyGem, nameLocation, new NonUpdatableLocation(context));
            }
        }
    }

    [GeneratedRegex("""^[A-Za-z0-9][A-Za-z0-9_.-]*$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GemNameRegex();

    // Bundler splits the version and the platform on the first '-'
    [GeneratedRegex("""^ {4}(?<name>[A-Za-z0-9][A-Za-z0-9_.-]*) \((?<version>[^-)]+)(?:-(?<platform>[^)]+))?\)$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LockDependencyRegex();
}
