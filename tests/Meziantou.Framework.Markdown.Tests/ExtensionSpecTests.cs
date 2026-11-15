namespace Meziantou.Framework.Markdown.Tests;

/// <summary>
/// The examples of the extensions of <c>Meziantou.Framework.Markdown</c> 1.x, for the extensions the parser supports. A
/// specification lists groups of extensions separated by <c>|</c>, and each group is a set of extensions separated by
/// <c>+</c>: every example is rendered with each group, as the tests of the 1.x library do.
/// </summary>
public sealed class ExtensionSpecTests
{
    private static readonly (string File, string Extensions)[] Specifications =
    [
        ("AlertBlockSpecs.md", "alerts|advanced"),
        ("PipeTableSpecs.md", "pipetables|advanced"),
        ("PipeTableGfmSpecs.md", "gfm-pipetables"),
        ("GfmTableSpecs.md", "gfm-pipetables"),
        ("FootnotesSpecs.md", "footnotes|advanced"),
        ("GenericAttributesSpecs.md", "attributes|advanced"),
        ("EmphasisExtraSpecs.md", "emphasisextras|advanced"),
        ("HardlineBreakSpecs.md", "hardlinebreak|advanced+hardlinebreak"),
        ("GridTableSpecs.md", "gridtables|advanced"),
        ("CustomContainerSpecs.md", "customcontainers+attributes|advanced"),
        ("DefinitionListSpecs.md", "definitionlists+attributes|advanced"),
        ("EmojiSpecs.md", "pipetables+emojis|advanced+emojis"),
        ("AbbreviationSpecs.md", "abbreviations|advanced"),
        ("ListExtraSpecs.md", "listextras|advanced"),
        ("MathSpecs.md", "mathematics|advanced"),
        ("BootstrapSpecs.md", "bootstrap+pipetables+figures+attributes+alerts"),
        ("MediaSpecs.md", "medialinks|advanced+medialinks"),
        ("SmartyPantsSpecs.md", "pipetables+smartypants|advanced+smartypants"),
        ("AutoIdentifierSpecs.md", "autoidentifiers|advanced"),
        ("TaskListSpecs.md", "tasklists|advanced"),
        ("DiagramsSpecs.md", "diagrams|advanced"),
        ("NoHtmlSpecs.md", "nohtml"),
        ("YamlSpecs.md", "yaml"),
        ("AutoLinks.md", "autolinks|advanced"),
        ("JiraLinks.md", "jiralinks"),
        ("GlobalizationSpecs.md", "globalization+advanced+emojis"),
        ("FigureFooterAndCiteSpecs.md", "figures+footers+citations|advanced"),
        ("CJKFriendlyEmphasis.md", "cjk-friendly-emphasis"),
    ];

    // The extensions that the parser supports, by the name the specifications give them
    private static readonly Dictionary<string, Func<MarkdownParseOptions, MarkdownParseOptions>> SupportedExtensions = new(StringComparer.Ordinal)
    {
        ["gfm-pipetables"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Tables },
        ["pipetables"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.PipeTables },
        ["gridtables"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.GridTables },
        ["medialinks"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.MediaLinks },
        ["autolinks"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.MarkdigAutolinks },
        ["advanced"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Advanced },
        ["emojis"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Emoji },
        ["bootstrap"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Bootstrap },
        ["globalization"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Globalization },
        ["cjk-friendly-emphasis"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.CjkFriendlyEmphasis },
        ["jiralinks"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.JiraLinks, JiraLinks = new MarkdownJiraLinkOptions { BaseUrl = "http://your.company.abc" } },
        ["tasklists"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.TaskLists },
        ["hardlinebreak"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.HardLineBreaks },
        ["nohtml"] = options => options with { DisableHtml = true },
        ["emphasisextras"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.EmphasisExtras },
        ["yaml"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.YamlFrontMatter },
        ["diagrams"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Diagrams },
        ["mathematics"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Mathematics },
        ["alerts"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Alerts },
        ["abbreviations"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Abbreviations },
        ["footnotes"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Footnotes },
        ["attributes"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.GenericAttributes },
        ["customcontainers"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.CustomContainers },
        ["definitionlists"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.DefinitionLists },
        ["listextras"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.ListExtras },
        ["smartypants"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.SmartyPants },
        ["figures"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Figures },
        ["footers"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Footers },
        ["citations"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.Citations },
        ["autoidentifiers"] = options => options with { Extensions = options.Extensions | MarkdownExtensions.AutoIdentifiers },
    };

    public static TheoryData<string, int, string> SupportedExamples()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var (file, extensions) in Specifications)
        {
            var examples = LoadExamples(file);
            foreach (var group in extensions.Split('|'))
            {
                if (GetOptions(group) is null)
                {
                    continue;
                }

                foreach (var example in examples)
                {
                    data.Add(file, example.Number, group);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SupportedExamples))]
    public void RendersTheHtmlOfTheExtensionSpecifications(string file, int number, string extensions)
    {
        var example = LoadExamples(file)[number - 1];
        var tree = MarkdownSyntaxTree.ParseText(example.Markdown, GetOptions(extensions));

        Assert.Equal(example.Markdown, tree.GetRoot().ToFullString());
        Assert.Equal(Compact(example.Html), Compact(MarkdownHtmlRenderer.ToHtml(tree)), $"{file}, example {number} ({example.Section}), with {extensions}");
    }

    [Theory]
    [MemberData(nameof(SupportedExamples))]
    public void FormattingKeepsTheHtmlOfTheExtensionSpecifications(string file, int number, string extensions)
    {
        var example = LoadExamples(file)[number - 1];

        CommonMarkSpec.AssertFormattingKeepsTheHtml(example.Markdown, GetOptions(extensions)!, $"{file}, example {number} ({example.Section}), with {extensions}");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<CommonMarkSpec.Example>> LoadedExamples = new(StringComparer.Ordinal);

    private static List<CommonMarkSpec.Example> LoadExamples(string file) => LoadedExamples.GetOrAdd(file, static file => CommonMarkSpec.Load(Path.Combine("Extensions", file)));

    private static MarkdownParseOptions? GetOptions(string group)
    {
        var options = MarkdownParseOptions.Default;
        foreach (var name in group.Split('+'))
        {
            if (!SupportedExtensions.TryGetValue(name, out var configure))
            {
                return null;
            }

            options = configure(options);
        }

        return options;
    }

    // The whitespace around the content of a list item is not significant, as in the tests of the 1.x library
    private static string Compact(string html)
    {
        html = html.ReplaceLineEndings("\n").Trim();
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+</li>", "</li>", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        return System.Text.RegularExpressions.Regex.Replace(html, @"<li>\s+", "<li>", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
    }
}
