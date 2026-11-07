// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.Emoji;
using Meziantou.Framework.Markdown.Extensions.EmphasisExtras;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestEmojiEmphasis
{
    [Theory]
    [InlineData("**Non-goals (explicitly out of scope for this plan):**", "<strong>Non-goals (explicitly out of scope for this plan):</strong>")]
    [InlineData("*Non-goals (explicitly out of scope for this plan):*", "<em>Non-goals (explicitly out of scope for this plan):</em>")]
    [InlineData("***Non-goals (explicitly out of scope for this plan):***", "<em><strong>Non-goals (explicitly out of scope for this plan):</strong></em>")]
    [InlineData("*:*", "<em>:</em>")]
    [InlineData("**:**", "<strong>:</strong>")]
    [InlineData("*outer **inner):***", "<em>outer <strong>inner):</strong></em>")]
    [InlineData("*a* :*", "<em>a</em> 😗")]
    [InlineData(":*", "😗")]
    [InlineData("(:*)", "(😗)")]
    [InlineData(":**", "😗*")]
    [InlineData(":***", "😗**")]
    [InlineData(":*text", "😗text")]
    [InlineData(":*text*", ":<em>text</em>")]
    [InlineData("*text :* more*", "<em>text :</em> more*")]
    [InlineData("*:kissing:*", "<em>😗</em>")]
    [InlineData("**:)**", "<strong>😃</strong>")]
    [InlineData("*text :-* more*", "<em>text 😗more</em>")]
    [InlineData(@"\*text :*", "*text 😗")]
    [InlineData(@"*text :\*", "*text :*")]
    [InlineData("`*text :*`", "<code>*text :*</code>")]
    [InlineData("[*text):*](url)", "<a href=\"url\"><em>text):</em></a>")]
    [InlineData("*text [link](url):*", "<em>text <a href=\"url\">link</a>:</em>")]
    [InlineData("*outside [inside :*](url)", "*outside <a href=\"url\">inside 😗</a>")]
    [InlineData("[*inside](url) :*", "<a href=\"url\">*inside</a> 😗")]
    [InlineData(":* :* **:** :*", "😗 😗 <strong>:</strong> 😗")]
    [InlineData("**:***", "<strong>:</strong>*")]
    [InlineData("*:**", "<em>:</em>*")]
    [InlineData("*a\ntext):*", "<em>a\ntext):</em>")]
    [InlineData(":*\n:*", "😗\n😗")]
    [InlineData("![*text):*](url)", "<img src=\"url\" alt=\"text):\" />")]
    [InlineData("[text :*][missing]", "[text 😗][missing]")]
    public void EmphasisTakesPriority(string markdown, string expected)
    {
        foreach (bool trackTrivia in new[] { false, true })
        {
            var builder = new MarkdownPipelineBuilder().UseEmojiAndSmiley();
            if (trackTrivia) builder.EnableTrackTrivia();
            Assert.Equal($"<p>{expected}</p>\n", MarkdownConverter.ToHtml(markdown, builder.Build()));
        }

        TestRoundtrip.RoundTrip(markdown, new MarkdownPipelineBuilder().UseEmojiAndSmiley());
    }

    [Theory]
    [InlineData("_text):_", "<em>text):</em>")]
    [InlineData("~~text):~~", "<del>text):</del>")]
    [InlineData("++text):++", "<ins>text):</ins>")]
    [InlineData("==text):==", "<mark>text):</mark>")]
    [InlineData("^text):^", "<sup>text):</sup>")]
    [InlineData(":_ :~ :+ := :^", "emoji emoji emoji emoji emoji")]
    public void CustomMappingsRespectConfiguredEmphasis(string markdown, string expected)
    {
        var mapping = new EmojiMapping(new Dictionary<string, string>
        {
            [":_"] = "emoji", [":~"] = "emoji", [":+"] = "emoji",
            [":="] = "emoji", [":^"] = "emoji"
        }, new Dictionary<string, string>());
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley(customEmojiMapping: mapping)
            .UseEmphasisExtras(EmphasisExtraOptions.Default).Build();
        Assert.Equal($"<p>{expected}</p>\n", MarkdownConverter.ToHtml(markdown, pipeline));

        TestRoundtrip.RoundTrip(markdown, new MarkdownPipelineBuilder().UseEmojiAndSmiley(customEmojiMapping: mapping).UseEmphasisExtras(EmphasisExtraOptions.Default));
    }

    [Fact]
    public void EmphasisCanBeDisabled()
    {
        var builder = new MarkdownPipelineBuilder().UseEmojiAndSmiley();
        builder.InlineParsers.Remove(builder.InlineParsers.Find<EmphasisInlineParser>()!);
        // Register explicitly: the extension normally inserts before the emphasis parser.
        builder.InlineParsers.Add(new EmojiParser(new EmojiMapping()));
        Assert.Equal("<p>**text)😗*</p>\n", MarkdownConverter.ToHtml("**text):**", builder.Build()));
    }

    [Fact]
    public void SmileysCanBeDisabled()
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley(enableSmileys: false).Build();
        Assert.Equal("<p><em>text):</em> :*</p>\n", MarkdownConverter.ToHtml("*text):* :*", pipeline));
    }

    [Fact]
    public void EmojiDoesNotChangeEmphasisStructure()
    {
        var plain = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();
        var emoji = new MarkdownPipelineBuilder().UsePreciseSourceLocation().UseEmojiAndSmiley().Build();
        string[] runs = ["", "*", "**", "***", "****", "_", "__", "___"];
        string[] contents = ["text):", ":", "text :", "text):* more):", "[text):*](url):", "`code*` :", "text\n:"];
        foreach (var opening in runs)
        foreach (var middle in contents)
        foreach (var closing in runs)
        foreach (var ending in new[] { "", "tail", " tail*", " :*", "&amp;", "&#32;" })
        {
            var markdown = $"prefix {opening}{middle}{closing}{ending}";
            var expected = MarkdownConverter.Parse(markdown, plain).Descendants<EmphasisInline>()
                .Select(x => (x.DelimiterChar, x.DelimiterCount, x.Span)).ToArray();
            var actual = MarkdownConverter.Parse(markdown, emoji).Descendants<EmphasisInline>()
                .Select(x => (x.DelimiterChar, x.DelimiterCount, x.Span)).ToArray();
            Assert.Equal(expected, actual, message: markdown);
        }
    }

    [Theory]
    [InlineData(":* tail", 0)]
    [InlineData("text :** tail", 5)]
    [InlineData("*a* :*", 4)]
    [InlineData("[link :*](url)", 6)]
    public void DeferredEmojiRetainsSourceLocation(string markdown, int start)
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().UsePreciseSourceLocation().Build();
        var document = MarkdownConverter.Parse(markdown, pipeline);
        var emoji = document.Descendants<EmojiInline>().Single();
        Assert.Equal(new SourceSpan(start, start + 1), emoji.Span);
        Assert.Equal(0, emoji.Line);
        Assert.Equal(start, emoji.Column);
        Assert.Equal(":*", emoji.Match);
        foreach (var literal in document.Descendants<LiteralInline>().Where(x => x is not EmojiInline))
        {
            Assert.Equal(markdown.Substring(literal.Span.Start, literal.Span.Length), literal.Content.ToString());
        }
    }

    [Fact]
    public void EmojiAndEmphasisStayWithinTableCells()
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().UsePipeTables().Build();
        var html = MarkdownConverter.ToHtml("| A | B |\n| - | - |\n| *text):* | :* |\n| *text | :* |", pipeline);
        Assert.Contains("<td><em>text):</em></td>\n<td>😗</td>", html);
        Assert.Contains("<td>*text</td>\n<td>😗</td>", html);
    }

    [Fact]
    public void UnambiguousDefaultMappingsAreUnchanged()
    {
        var shortcodes = EmojiMapping.GetDefaultEmojiShortcodeToUnicode();
        var smileys = EmojiMapping.GetDefaultSmileyToEmojiShortcode();
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().Build();
        foreach (var mapping in shortcodes.Concat(smileys.Select(x => new KeyValuePair<string, string>(x.Key, shortcodes[x.Value]))))
        {
            var markdown = $"prefix {mapping.Key} suffix";
            Assert.Equal($"<p>prefix {mapping.Value} suffix</p>\n", MarkdownConverter.ToHtml(markdown, pipeline), message: markdown);
        }
    }

    [Theory]
    [InlineData(":**", "long")]
    [InlineData("**:**", "<strong>:</strong>")]
    [InlineData("*", "star")]
    public void LongestMatchAndDelimiterOnlyMappingsArePreserved(string markdown, string expected)
    {
        var shortcodes = new Dictionary<string, string>
        {
            [":*"] = "short", [":**"] = "long"
        };
        // Only test delimiter-only mappings in isolation: they intentionally override emphasis.
        if (markdown == "*")
        {
            shortcodes.Add("*", "star");
        }
        var mapping = new EmojiMapping(shortcodes, new Dictionary<string, string>());
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley(customEmojiMapping: mapping).Build();
        Assert.Equal($"<p>prefix {expected}</p>\n", MarkdownConverter.ToHtml($"prefix {markdown}", pipeline));
    }

    [Fact]
    public void OtherRenderersSeeResolvedEmojiAndEmphasis()
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().Build();
        var markdown = "*text):* :*";
        Assert.Equal("text): 😗", MarkdownConverter.ToPlainText(markdown, pipeline).Trim());
        Assert.Equal("*text):* 😗", MarkdownConverter.Normalize(markdown, pipeline: pipeline).Trim());
    }

    // Attributes bind to the emphasis delimiter, even if it ultimately remains unmatched.
    // Use a named shortcode to attach attributes to the surrounding paragraph instead.
    [Theory]
    [InlineData(":*{.kiss}", "<p>😗</p>\n")]
    [InlineData(":kissing:{.kiss}", "<p class=\"kiss\">😗</p>\n")]
    [InlineData("*text):*{.label}", "<p><em class=\"label\">text):</em></p>\n")]
    public void GenericAttributesRespectResolvedSyntax(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().UseGenericAttributes().Build();
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }
}
