---
title: Getting started
description: Install the package, highlight a snippet, put it in a page, and add the CSS that colors it.
---

## Install the package

Add the `Meziantou.Framework.SyntaxHighlighting` package to a project:

```bash
dotnet add package Meziantou.Framework.SyntaxHighlighting
```

## Highlight a snippet

[SyntaxHighlighter.Highlight](cref:Meziantou.Framework.SyntaxHighlighting.SyntaxHighlighter.Highlight) takes the code and the identifier of its language, and returns HTML:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_Highlight;format=md-fenced-code-block;dedent=true -->
```csharp
var html = SyntaxHighlighter.Highlight(
    "public class MyClass { }",
    "csharp");

Assert.Equal("""<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">MyClass</span> { }""", html);
```
<!-- endref -->

The identifier is case-insensitive, and each language has aliases, such as `cs` and `c#` for `csharp`. See
[Supported languages](04-SupportedLanguages.md). [Highlight](cref:Meziantou.Framework.SyntaxHighlighting.SyntaxHighlighter.Highlight) throws a `NotSupportedException` for an identifier it does
not know: use [TryHighlight](cref:Meziantou.Framework.SyntaxHighlighting.SyntaxHighlighter.TryHighlight) when the identifier comes from your users.

## Put it in a page

The output is the content of a code block, not a whole block: put it in a `<pre><code>` element. The code is
HTML-encoded, so `<`, `>` and `"` in the code are safe in the page:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_HtmlPage;format=md-fenced-code-block;dedent=true -->
```csharp
var code = """var tag = "<b>";""";
var html = $"""<pre><code class="hljs language-csharp">{SyntaxHighlighter.Highlight(code, "csharp")}</code></pre>""";

Assert.Equal("""<pre><code class="hljs language-csharp"><span class="hljs-keyword">var</span> tag = <span class="hljs-string">&quot;&lt;b&gt;&quot;</span>;</code></pre>""", html);
```
<!-- endref -->

The `hljs` and `language-csharp` classes on the `<code>` element are not required. Themes written for the `hljs-` class
names use `hljs` to set the background and the default color of the block.

## Add the CSS

The package only produces HTML: the colors come from your CSS. Write rules for the classes you want to color, or use a
theme written for the `hljs-` class names. See [Styling](03-Styling.md).
