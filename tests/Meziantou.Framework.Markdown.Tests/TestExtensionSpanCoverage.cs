using Meziantou.Framework.Markdown.Extensions.Abbreviations;
using Meziantou.Framework.Markdown.Extensions.Alerts;
using Meziantou.Framework.Markdown.Extensions.DefinitionLists;
using Meziantou.Framework.Markdown.Extensions.Emoji;
using Meziantou.Framework.Markdown.Extensions.Figures;
using Meziantou.Framework.Markdown.Extensions.Footers;
using Meziantou.Framework.Markdown.Extensions.Footnotes;
using Meziantou.Framework.Markdown.Extensions.JiraLinks;
using Meziantou.Framework.Markdown.Extensions.Mathematics;
using Meziantou.Framework.Markdown.Extensions.SmartyPants;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Extensions.TaskLists;
using Meziantou.Framework.Markdown.Extensions.Yaml;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public sealed class TestExtensionSpanCoverage
{
    public sealed class ExtensionSpanCase(
        string name,
        Action<MarkdownPipelineBuilder> configurePipeline,
        string markdown,
        Action<MarkdownDocument>? validate,
        bool validateSpanTree = true)
    {
        public string Name { get; } = name;
        public Action<MarkdownPipelineBuilder> ConfigurePipeline { get; } = configurePipeline;
        public string Markdown { get; } = markdown;
        public Action<MarkdownDocument>? Validate { get; } = validate;
        public bool ValidateSpanTree { get; } = validateSpanTree;

        public override string ToString() => $"Extension_{Name}_MaintainsValidSpans";
    }

    [Theory]
    [MemberData(nameof(GetExtensionSpanCases))]
    public void ExtensionSpanTreeIsValid(ExtensionSpanCase testCase)
    {
        var builder = new MarkdownPipelineBuilder
        {
            PreciseSourceLocation = true
        };
        testCase.ConfigurePipeline(builder);

        var document = MarkdownConverter.Parse(testCase.Markdown.ReplaceLineEndings("\n"), builder.Build());

        if (testCase.ValidateSpanTree)
        {
            Assert.True(document.HasValidSpan(recursive: true), message: $"{testCase.Name} has invalid container spans");
        }

        testCase.Validate?.Invoke(document);
    }

    [Fact]
    public void JiraLinkSpanMatchesToken()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            PreciseSourceLocation = true
        }.UseJiraLinks(new JiraLinkOptions("https://jira.example.com")).Build();

        var document = MarkdownConverter.Parse("ABC-123", pipeline);
        var jiraLink = document.Descendants<JiraLink>().FirstOrDefault();
        Assert.NotNull(jiraLink);
        Assert.Equal(new SourceSpan(0, 6), jiraLink!.Span);

        var literal = jiraLink.FirstChild as LiteralInline;
        Assert.NotNull(literal);
        Assert.Equal(new SourceSpan(0, 6), literal!.Span);
    }

    [Fact]
    public void TaskListSpanMatchesCheckboxToken()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            PreciseSourceLocation = true
        }.UseTaskLists().Build();

        var document = MarkdownConverter.Parse("- [x] done", pipeline);
        var task = document.Descendants<TaskList>().FirstOrDefault();
        Assert.NotNull(task);
        Assert.Equal(new SourceSpan(2, 4), task!.Span);
    }

    [Fact]
    public void AlertBlockSpanCoversSourceQuote()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            PreciseSourceLocation = true
        }.UseAlertBlocks().Build();

        var document = MarkdownConverter.Parse("> [!NOTE]\n> body", pipeline);
        var alert = document.Descendants<AlertBlock>().FirstOrDefault();
        Assert.NotNull(alert);
        Assert.Equal(0, alert!.Span.Start);
        Assert.True(alert.Span.End > 0);

        var paragraph = alert.Descendants<ParagraphBlock>().FirstOrDefault();
        Assert.NotNull(paragraph);
        Assert.True(paragraph!.Span.Start >= alert.Span.Start);
        Assert.True(paragraph.Span.End <= alert.Span.End);
    }

    [Fact]
    public void YamlFrontMatterSpanCoversFrontMatterContent()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            PreciseSourceLocation = true
        }.UseYamlFrontMatter().Build();

        var document = MarkdownConverter.Parse("---\na: 1\n---\ntext", pipeline);
        var yaml = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        Assert.NotNull(yaml);
        Assert.Equal(0, yaml!.Span.Start);
        Assert.True(yaml.Span.End >= 7);
    }

    public static TheoryData<ExtensionSpanCase> GetExtensionSpanCases() => new(EnumerateExtensionSpanCases());

    private static IEnumerable<ExtensionSpanCase> EnumerateExtensionSpanCases()
    {
        yield return Case(
            "AlertBlocks",
            builder => builder.UseAlertBlocks(),
            "> [!NOTE]\n> body",
            document => AssertNodesHaveNonEmptySpan<AlertBlock>(document));

        yield return Case(
            "AutoLinks",
            builder => builder.UseAutoLinks(),
            "http://example.com",
            document =>
            {
                var autoLink = document.Descendants<LinkInline>().FirstOrDefault(link => link.IsAutoLink);
                Assert.NotNull(autoLink);
                Assert.False(autoLink.Span.IsEmpty);
                Assert.Equal(autoLink.Span, autoLink.UrlSpan);
            });

        yield return Case(
            "NonAsciiNoEscape",
            builder => builder.UseNonAsciiNoEscape(),
            "Café");

        yield return Case(
            "YamlFrontMatter",
            builder => builder.UseYamlFrontMatter(),
            "---\na: 1\n---\ntext",
            document => AssertNodesHaveNonEmptySpan<YamlFrontMatterBlock>(document));

        yield return Case(
            "PragmaLines",
            builder => builder.UsePragmaLines(),
            "# Heading\n\nParagraph");

        yield return Case(
            "Diagrams",
            builder => builder.UseDiagrams(),
            "```mermaid\ngraph TD;\n```");

        yield return Case(
            "TaskLists",
            builder => builder.UseTaskLists(),
            "- [x] done",
            document => AssertNodesHaveNonEmptySpan<TaskList>(document));

        yield return Case(
            "CustomContainers",
            builder => builder.UseCustomContainers(),
            ":::\nvalue\n:::",
            document => AssertNodesHaveNonEmptySpan<Meziantou.Framework.Markdown.Extensions.CustomContainers.CustomContainer>(document));

        yield return Case(
            "MediaLinks",
            builder => builder.UseMediaLinks(),
            "[video](https://www.youtube.com/watch?v=dQw4w9WgXcQ)",
            document => AssertNodesHaveNonEmptySpan<LinkInline>(document));

        yield return Case(
            "AutoIdentifiers",
            builder => builder.UseAutoIdentifiers(),
            "# Heading",
            document =>
            {
                var heading = document.Descendants<HeadingBlock>().FirstOrDefault();
                Assert.NotNull(heading);
                Assert.NotEmpty(heading.GetAttributes().Id);
            });

        yield return Case(
            "SmartyPants",
            builder => builder.UseSmartyPants(),
            "<<a>>",
            document => AssertNodesHaveNonEmptySpan<SmartyPant>(document));

        yield return Case(
            "Bootstrap",
            builder => builder.UseBootstrap(),
            "![alt](img.png)");

        yield return Case(
            "Mathematics",
            builder => builder.UseMathematics(),
            "$a$",
            document => AssertNodesHaveNonEmptySpan<MathInline>(document));

        yield return Case(
            "Figures",
            builder => builder.UseFigures(),
            "^^^\ntext\n^^^",
            document => AssertNodesHaveNonEmptySpan<Figure>(document));

        yield return Case(
            "Abbreviations",
            builder => builder.UseAbbreviations(),
            "*[HTML]: HyperText Markup Language\n\nHTML",
            document => AssertNodesHaveNonEmptySpan<AbbreviationInline>(document));

        yield return Case(
            "DefinitionLists",
            builder => builder.UseDefinitionLists(),
            "a0\n:   1234",
            document => AssertNodesHaveNonEmptySpan<DefinitionList>(document));

        yield return Case(
            "PipeTables",
            builder => builder.UsePipeTables(),
            "a|b\n-|-\n1|2",
            document => AssertNodesHaveNonEmptySpan<Table>(document));

        yield return Case(
            "GridTables",
            builder => builder.UseGridTables(),
            "+-+-+\n|a|b|\n+=+=+\n|c|d|\n+-+-+",
            document => AssertNodesHaveNonEmptySpan<Table>(document));

        yield return Case(
            "Citations",
            builder => builder.UseCitations(),
            "\"\"text\"\"",
            document =>
            {
                var citation = document.Descendants<EmphasisInline>().FirstOrDefault(x => x.DelimiterChar == '"' && x.DelimiterCount == 2);
                Assert.NotNull(citation);
                Assert.False(citation.Span.IsEmpty);
            });

        yield return Case(
            "Footers",
            builder => builder.UseFooters(),
            "^^ one\n^^ two",
            document => AssertNodesHaveNonEmptySpan<FooterBlock>(document));

        yield return Case(
            "Footnotes",
            builder => builder.UseFootnotes(),
            "[^1]: note\n\nref[^1]",
            document =>
            {
                AssertNodesHaveNonEmptySpan<FootnoteGroup>(document);
                AssertNodesHaveNonEmptySpan<Footnote>(document);
                var links = document.Descendants<FootnoteLink>().ToList();
                Assert.NotEmpty(links);
            });

        yield return Case(
            "Hardlines",
            builder => builder.UseSoftlineBreakAsHardlineBreak(),
            "a\nb",
            document =>
            {
                var lineBreak = document.Descendants<LineBreakInline>().FirstOrDefault();
                Assert.NotNull(lineBreak);
                Assert.True(lineBreak.IsHard);
            });

        yield return Case(
            "EmphasisExtras",
            builder => builder.UseEmphasisExtras(),
            "~~text~~",
            document =>
            {
                var emphasis = document.Descendants<EmphasisInline>().FirstOrDefault(x => x.DelimiterChar == '~' && x.DelimiterCount == 2);
                Assert.NotNull(emphasis);
                Assert.False(emphasis.Span.IsEmpty);
            });

        yield return Case(
            "ListExtras",
            builder => builder.UseListExtras(),
            "A. item",
            document =>
            {
                var list = document.Descendants<ListBlock>().FirstOrDefault();
                Assert.NotNull(list);
                Assert.False(list.Span.IsEmpty);
                Assert.True(list.IsOrdered);
            });

        yield return Case(
            "GenericAttributes",
            builder => builder.UseGenericAttributes(),
            "text{#custom-id}",
            document =>
            {
                var paragraph = document.Descendants<ParagraphBlock>().FirstOrDefault();
                Assert.NotNull(paragraph);
                var attributes = paragraph.TryGetAttributes();
                Assert.NotNull(attributes);
                Assert.False(attributes.Span.IsEmpty);
            });

        yield return Case(
            "EmojiAndSmiley",
            builder => builder.UseEmojiAndSmiley(),
            ":)",
            document => AssertNodesHaveNonEmptySpan<EmojiInline>(document));

        yield return Case(
            "NoFollowLinks",
            builder => builder.UseReferralLinks("nofollow"),
            "[link](https://example.com)",
            document => AssertNodesHaveNonEmptySpan<LinkInline>(document));

        yield return Case(
            "ReferralLinks",
            builder => builder.UseReferralLinks("nofollow", "noopener"),
            "[link](https://example.com)",
            document => AssertNodesHaveNonEmptySpan<LinkInline>(document));

        yield return Case(
            "JiraLinks",
            builder => builder.UseJiraLinks(new JiraLinkOptions("https://jira.example.com")),
            "ABC-123",
            document => AssertNodesHaveNonEmptySpan<JiraLink>(document));

        yield return Case(
            "Globalization",
            builder => builder.UseGlobalization(),
            "## Héllo");
    }

    private static ExtensionSpanCase Case(
        string name,
        Action<MarkdownPipelineBuilder> configurePipeline,
        string markdown,
        Action<MarkdownDocument>? validate = null,
        bool validateSpanTree = true)
    {
        return new ExtensionSpanCase(name, configurePipeline, markdown, validate, validateSpanTree);
    }

    private static void AssertNodesHaveNonEmptySpan<T>(MarkdownDocument document) where T : MarkdownObject
    {
        var nodes = document.Descendants<T>().ToList();
        Assert.NotEmpty(nodes, message: $"Expected at least one node of type `{typeof(T).Name}`");
        foreach (var node in nodes)
        {
            Assert.False(node.Span.IsEmpty, message: $"Node `{typeof(T).Name}` has an empty span");
        }
    }
}
