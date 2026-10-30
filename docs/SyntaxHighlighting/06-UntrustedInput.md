---
title: Untrusted input
description: Highlight code and language identifiers written by your users safely, and bound the time a hostile input can take.
---

Code from a comment, a forum post or a Markdown document written by your users needs three precautions: the output must
be safe to put in a page, an unknown language must not fail the request, and a hostile input must not take too long.

## The output is encoded

Everything that is not markup written by the highlighter is HTML-encoded: `<`, `>`, `&`, `"` and `'`. The output is safe
to put in a page, whatever the code contains. See [Put it in a page](02-GettingStarted.md#put-it-in-a-page).

The highlighter writes the content of a code block only: encode the attributes and the elements you write around it
yourself, such as a `language-…` class built from a language identifier of your users.

## Unknown languages

[Highlight](cref:Meziantou.Framework.SyntaxHighlighting.SyntaxHighlighter.Highlight) throws a `NotSupportedException` when it does not know the language. When the identifier comes from your
users, such as the language of a Markdown code block, use [TryHighlight](cref:Meziantou.Framework.SyntaxHighlighting.SyntaxHighlighter.TryHighlight), or check it with [IsSupported](cref:Meziantou.Framework.SyntaxHighlighting.SyntaxHighlighter.IsSupported), and render the
code as plain text otherwise:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_TryHighlight;format=md-fenced-code-block;dedent=true -->
```csharp
var code = "<b>Hello</b>";
var language = "not-a-language";
if (!SyntaxHighlighter.TryHighlight(code, language, out var html))
{
    html = WebUtility.HtmlEncode(code); // render it as plain text
}

Assert.Equal("&lt;b&gt;Hello&lt;/b&gt;", html);
```
<!-- endref -->

`WebUtility` is in the `System.Net` namespace.

## Bound the matching time

The grammars are regular expressions, and a hostile input can make some of them slow. Set
[HighlightOptions.MatchTimeout](cref:Meziantou.Framework.SyntaxHighlighting.HighlightOptions.MatchTimeout) to bound each match. When a match takes longer, the code is returned as plain, encoded
text rather than blocking the caller:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_MatchTimeout;format=md-fenced-code-block;dedent=true -->
```csharp
var code = "public class MyClass { }";
var language = "csharp";
var html = SyntaxHighlighter.Highlight(code, language, new HighlightOptions { MatchTimeout = TimeSpan.FromSeconds(1) });
```
<!-- endref -->

- A timeout makes matching slower, about twice as slow, which is why there is none by default.
- Choose a value that leaves room for large documents on a busy machine: a match that times out on a valid document shows
  it as plain text.
- Keep the value constant in your application: the grammars are compiled again for each timeout, and only the grammars
  of the last few timeouts are kept in memory.
- The timeout bounds each match, not the whole call: also limit the size of the code you accept.

The timeout must be positive and less than 24 days, or `Timeout.InfiniteTimeSpan`; any other value throws an
`ArgumentOutOfRangeException`.
