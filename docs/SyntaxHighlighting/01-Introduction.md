---
title: Introduction
description: Highlight source code in .NET and get HTML with CSS classes, for documentation sites, blogs and web applications.
---

`Meziantou.Framework.SyntaxHighlighting` highlights source code in .NET. It returns HTML in which each token is a
`<span>` with a CSS class, such as `hljs-keyword` or `hljs-string`: you style the classes with your own CSS, or with an
existing theme. The highlighting runs on the server or at build time, so the page needs no JavaScript to show colored
code.

## What you can do with it

- **Highlight code in more than a hundred languages**, from C# and TypeScript to SQL, YAML and Dockerfiles. See
  [Getting started](02-GettingStarted.md) and [Supported languages](04-SupportedLanguages.md).
- **Style the output** with your own CSS or a theme written for the `hljs-` class names, and change the prefix of the
  class names. See [Styling](03-Styling.md).
- **Highlight code that has mistakes**: an invalid character does not stop the highlighting. See
  [Invalid code](05-InvalidCode.md).
- **Highlight code written by your users**: the output is HTML-encoded, and you can bound the time a hostile input takes.
  See [Untrusted input](06-UntrustedInput.md).

## How it works

Each language has a grammar made of regular expressions. The highlighter matches them against the code, and wraps each
match in a `<span>` whose class names its scope, such as a keyword, a string or a comment. Everything else is copied as
HTML-encoded text. The highlighter does not parse the code into a syntax tree: to analyze code, see
[Language](../Language/01-Introduction.md).

`SyntaxHighlighter` and `HighlightOptions`, in the [Meziantou.Framework.SyntaxHighlighting](cref:Meziantou.Framework.SyntaxHighlighting) namespace, are the whole
API.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and highlight your first snippet.
- [Styling](03-Styling.md): the CSS classes of the output.
- [Untrusted input](06-UntrustedInput.md): what to do before you highlight code written by your users.
