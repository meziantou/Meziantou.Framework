using Meziantou.Framework.Markdown.Extensions.Emoji;

namespace Meziantou.Framework.Markdown.Tests;

public class TestCustomEmojis
{
    [Theory]
    [InlineData(":smiley:", "<p>♥</p>\n")]
    [InlineData(":confused:", "<p>:confused:</p>\n")] // default emoji does not work
    [InlineData(":/", "<p>:/</p>\n")] // default smiley does not work
    public void TestCustomEmoji(string input, string expected)
    {
        var emojiToUnicode = new Dictionary<string, string>();
        var smileyToEmoji = new Dictionary<string, string>();

        emojiToUnicode[":smiley:"] = "♥";

        var customMapping = new EmojiMapping(emojiToUnicode, smileyToEmoji);

        var pipeline = new MarkdownPipelineBuilder()
            .UseEmojiAndSmiley(customEmojiMapping: customMapping)
            .Build();

        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(":testheart:", "<p>♥</p>\n")]
    [InlineData("hello", "<p>♥</p>\n")]
    [InlineData(":confused:", "<p>:confused:</p>\n")] // default emoji does not work
    [InlineData(":/", "<p>:/</p>\n")] // default smiley does not work
    public void TestCustomSmiley(string input, string expected)
    {
        var emojiToUnicode = new Dictionary<string, string>();
        var smileyToEmoji = new Dictionary<string, string>();

        emojiToUnicode[":testheart:"] = "♥";
        smileyToEmoji["hello"] = ":testheart:";

        var customMapping = new EmojiMapping(emojiToUnicode, smileyToEmoji);

        var pipeline = new MarkdownPipelineBuilder()
            .UseEmojiAndSmiley(customEmojiMapping: customMapping)
            .Build();

        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(":smiley:", "<p>♥</p>\n")]
    [InlineData(":)", "<p>♥</p>\n")]
    [InlineData(":confused:", "<p>😕</p>\n")] // default emoji still works
    [InlineData(":/", "<p>😕</p>\n")] // default smiley still works
    public void TestOverrideDefaultWithCustomEmoji(string input, string expected)
    {
        var emojiToUnicode = EmojiMapping.GetDefaultEmojiShortcodeToUnicode();
        var smileyToEmoji = EmojiMapping.GetDefaultSmileyToEmojiShortcode();

        emojiToUnicode[":smiley:"] = "♥";

        var customMapping = new EmojiMapping(emojiToUnicode, smileyToEmoji);

        var pipeline = new MarkdownPipelineBuilder()
            .UseEmojiAndSmiley(customEmojiMapping: customMapping)
            .Build();

        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(":testheart:", "<p>♥</p>\n")]
    [InlineData("hello", "<p>♥</p>\n")]
    [InlineData(":confused:", "<p>😕</p>\n")] // default emoji still works
    [InlineData(":/", "<p>😕</p>\n")] // default smiley still works
    public void TestOverrideDefaultWithCustomSmiley(string input, string expected)
    {
        var emojiToUnicode = EmojiMapping.GetDefaultEmojiShortcodeToUnicode();
        var smileyToEmoji = EmojiMapping.GetDefaultSmileyToEmojiShortcode();

        emojiToUnicode[":testheart:"] = "♥";
        smileyToEmoji["hello"] = ":testheart:";

        var customMapping = new EmojiMapping(emojiToUnicode, smileyToEmoji);

        var pipeline = new MarkdownPipelineBuilder()
            .UseEmojiAndSmiley(customEmojiMapping: customMapping)
            .Build();

        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TestCustomEmojiValidation()
    {
        var emojiToUnicode = new Dictionary<string, string>();
        var smileyToEmoji = new Dictionary<string, string>();

        Assert.Throws<ArgumentNullException>(() => new EmojiMapping(null!, smileyToEmoji));
        Assert.Throws<ArgumentNullException>(() => new EmojiMapping(emojiToUnicode, null!));

        emojiToUnicode.Add("null-value", null!);
        Assert.Throws<ArgumentException>(() => new EmojiMapping(emojiToUnicode, smileyToEmoji));
        emojiToUnicode.Clear();

        smileyToEmoji.Add("null-value", null!);
        Assert.Throws<ArgumentException>(() => new EmojiMapping(emojiToUnicode, smileyToEmoji));
        smileyToEmoji.Clear();

        smileyToEmoji.Add("foo", "something-that-does-not-exist-in-emojiToUnicode");
        Assert.Throws<ArgumentException>(() => new EmojiMapping(emojiToUnicode, smileyToEmoji));
        smileyToEmoji.Clear();

        emojiToUnicode.Add("a", "aaa");
        emojiToUnicode.Add("b", "bbb");
        emojiToUnicode.Add("c", "ccc");
        smileyToEmoji.Add("a", "c"); // "a" already exists in emojiToUnicode
        Assert.Throws<ArgumentException>(() => new EmojiMapping(emojiToUnicode, smileyToEmoji));
    }

    [Theory]
    [InlineData("|test|\n|-|\n|:x:|", "<table>\n<thead>\n<tr>\n<th>test</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>❌</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("|test|\n|-|\n| :x: |", "<table>\n<thead>\n<tr>\n<th>test</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>❌</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("|test|\n|-|\n|1:x:|", "<table>\n<thead>\n<tr>\n<th>test</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>1:x:</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("|test|\n|-|\n|w:x:y|", "<table>\n<thead>\n<tr>\n<th>test</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>w:x:y</td>\n</tr>\n</tbody>\n</table>\n")]
    public void TestEmojiInPipeTable(string input, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseEmojiAndSmiley()
            .UsePipeTables()
            .Build();

        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestEmojiDoesNotBreakPipeTableAlignment(bool useEmojiFirst)
    {
        var input = "| Left | Center | Right |\n| ---- |:------:| -----:|\n| a | b | c |";
        var expected = "<table>\n<thead>\n<tr>\n<th>Left</th>\n<th style=\"text-align: center;\">Center</th>\n<th style=\"text-align: right;\">Right</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>a</td>\n<td style=\"text-align: center;\">b</td>\n<td style=\"text-align: right;\">c</td>\n</tr>\n</tbody>\n</table>\n";

        var pipelineBuilder = new MarkdownPipelineBuilder();
        if (useEmojiFirst)
        {
            pipelineBuilder.UseEmojiAndSmiley().UsePipeTables();
        }
        else
        {
            pipelineBuilder.UsePipeTables().UseEmojiAndSmiley();
        }

        var pipeline = pipelineBuilder.Build();
        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TestEmojiDoesNotBreakGridTableAlignment()
    {
        var input = "+-----+:---:+-----+\n|  A  | :x: |  C  |\n+-----+-----+-----+";
        var expected = "<table>\n<col style=\"width:33.33%\" />\n<col style=\"width:33.33%\" />\n<col style=\"width:33.33%\" />\n<tbody>\n<tr>\n<td>A</td>\n<td style=\"text-align: center;\">❌</td>\n<td>C</td>\n</tr>\n</tbody>\n</table>\n";

        var pipeline = new MarkdownPipelineBuilder()
            .UseGridTables()
            .UseEmojiAndSmiley()
            .Build();

        var actual = MarkdownConverter.ToHtml(input, pipeline);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TestPipeTableExtensionDoesNotSuppressNeutralFaceInParagraph()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmojiAndSmiley()
            .Build();

        var actual = MarkdownConverter.ToHtml("text :|", pipeline);
        Assert.Equal("<p>text 😐</p>\n", actual);
    }
}
