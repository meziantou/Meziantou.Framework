---
title: Invalid code
description: How the highlighter handles code that the grammar of its language does not allow.
---

The grammars flag some constructs as illegal, such as a character where the language does not allow one. By default, an
illegal lexeme is written as plain text and the rest of the code is still highlighted, so a snippet with a typo, or a
fragment cut from a larger file, is still readable.

Set `HighlightOptions.IgnoreIllegals` to `false` to get the whole result as plain text instead, as soon as the code has
an illegal lexeme:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_IgnoreIllegals;format=md-fenced-code-block;dedent=true -->
```csharp
var code = """{"a": 1};""";
var html = SyntaxHighlighter.Highlight(code, "json", new HighlightOptions { IgnoreIllegals = false });
Assert.Equal("{&quot;a&quot;: 1};", html);
```
<!-- endref -->

The result is still HTML-encoded, so it is safe to put in a page. Embedded languages, such as CSS in HTML or C# in Razor,
always ignore illegal lexemes.

The highlighter does not report errors: to check that code is valid, use a parser, such as the
[Language](../Language/01-Introduction.md) packages for the languages they support.
