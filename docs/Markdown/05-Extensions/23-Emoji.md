---
title: Emojis
description: 'Write emojis with shortcodes such as :rocket: and smileys such as :), or with a mapping of your own.'
---

[Emoji](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Emoji) lets people type emojis as text, as in chat
applications: shortcodes such as `:rocket:`, and smileys such as `:)` or `<3`, are written as their emoji when they are
not after a letter or a digit.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Emoji;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Emoji };
Assert.Equal("<p>Ship it \U0001F680 :)</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("Ship it :rocket: :)\n", options with { EmojiMapping = MarkdownEmojiMapping.DefaultWithoutSmileys })));

// A mapping of its own: shortcodes, and smileys for them
var mapping = new MarkdownEmojiMapping(new Dictionary<string, string> { [":ok:"] = "\u2705" }, new Dictionary<string, string> { ["(y)"] = ":ok:" });
Assert.Equal("<p>\u2705 \u2705 :rocket:</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(":ok: (y) :rocket:\n", options with { EmojiMapping = mapping })));

// The default shortcodes, with a few of the default smileys: -> stays as it is
var smileys = new[] { ":)", ";)" }.ToDictionary(smiley => smiley, smiley => MarkdownEmojiMapping.Default.Smileys[smiley], StringComparer.Ordinal);
var fewSmileys = new MarkdownEmojiMapping(MarkdownEmojiMapping.Default.Shortcodes, smileys);
Assert.Equal("<p>\U0001F680 \U0001F603 -&gt; b</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(":rocket: :) -> b\n", options with { EmojiMapping = fewSmileys })));
```
<!-- endref -->

Each one is a token of kind [EmojiToken](cref:Meziantou.Framework.Markdown.SyntaxKind.EmojiToken).

[EmojiMapping](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.EmojiMapping) chooses the shortcodes and the
smileys: [MarkdownEmojiMapping.Default](cref:Meziantou.Framework.Markdown.MarkdownEmojiMapping.Default) has the
shortcodes of GitHub, such as `:+1:` and its alias `:thumbsup:`, and the common smileys,
[DefaultWithoutSmileys](cref:Meziantou.Framework.Markdown.MarkdownEmojiMapping.DefaultWithoutSmileys) only its
shortcodes, so that `:)` stays as it is, and a mapping can be made from dictionaries.
[Shortcodes](cref:Meziantou.Framework.Markdown.MarkdownEmojiMapping.Shortcodes) and
[Smileys](cref:Meziantou.Framework.Markdown.MarkdownEmojiMapping.Smileys) give the dictionaries of a mapping, to make
another one from it: the default shortcodes with a few of its smileys, so that arrows such as `->` stay as they are in
text about code. A shortcode or a smiley cannot contain a line ending: the constructor throws an `ArgumentException`,
because a key that spans lines would match across the markers of the containers, such as the `>` of a block quote.
