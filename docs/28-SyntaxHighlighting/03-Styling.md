---
title: Styling
description: The CSS classes of the highlighted HTML, how to style them, and how to change their prefix.
---

The highlighter writes HTML and no style: each token is a `<span>` with a class, and your CSS gives it a color. Write
rules for the classes you need, such as `.hljs-keyword { color: #0033b3; }`, or use an existing theme written for the
`hljs-` class names.

## The class names

A class name is the prefix, `hljs-` by default, followed by the scope of the token:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_ScopeClasses;format=md-fenced-code-block;dedent=true -->
```csharp
var html = SyntaxHighlighter.Highlight("function add(a, b) { return a + b; }", "javascript");

Assert.Equal("""<span class="hljs-keyword">function</span> <span class="hljs-title function_">add</span>(<span class="hljs-params">a, b</span>) { <span class="hljs-keyword">return</span> a + b; }""", html);
```
<!-- endref -->

- A scope with a sub-scope, such as the name of a function, gives two classes: `hljs-title` and `function_`. Style
  `.hljs-title` to color every title, or `.hljs-title.function_` to color the functions only. Each level of sub-scope
  adds one more underscore: `class_`, then `inherited__`.
- Spans nest: a string can contain an interpolated expression, and a tag contains its name and attributes.
- Text that is not a token, such as punctuation and whitespace in most languages, is not in a span.

The grammars use these scopes most:

| Scope | Marks |
| --- | --- |
| `keyword`, `built_in`, `type`, `literal` | Reserved words, built-in functions and types, `true`, `null` and the like |
| `string`, `number`, `regexp`, `char`, `symbol` | Literal values |
| `comment`, `doctag` | Comments, and the documentation tags inside them |
| `title`, `params`, `function`, `class` | Names of the declared functions and types, and their parameters |
| `variable`, `attr`, `attribute`, `property` | Variables, attribute and property names |
| `meta`, `subst` | Preprocessor directives, annotations, and interpolated expressions inside strings |
| `tag`, `name` | Markup tags and their names |
| `selector-tag`, `selector-class`, `selector-id`, `selector-pseudo`, `selector-attr` | The parts of CSS selectors |
| `section`, `bullet`, `emphasis`, `strong`, `link`, `code`, `quote` | The parts of markup languages such as Markdown |
| `addition`, `deletion` | Added and removed lines of a diff |

## Embedded languages

Some languages contain others: CSS and JavaScript in HTML, C# in Razor. The embedded code is wrapped in a
`<span class="language-…">` with the name of its language, and highlighted with its own grammar:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_EmbeddedLanguage;format=md-fenced-code-block;dedent=true -->
```csharp
var html = SyntaxHighlighter.Highlight("<style>p { color: red }</style>", "html");

Assert.Equal("""<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css"><span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red }</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>""", html);
```
<!-- endref -->

The `language-` class has no prefix. Use it to style an embedded language differently, such as
`.language-css .hljs-attribute`.

## Change the prefix

`HighlightOptions.ClassPrefix` replaces `hljs-`, for instance to avoid a conflict with another stylesheet of the page:

<!-- ref:../../tests/Meziantou.Framework.SyntaxHighlighting.Tests/HighlighterTests.cs;region=SyntaxHighlighter_ClassPrefix;format=md-fenced-code-block;dedent=true -->
```csharp
var html = SyntaxHighlighter.Highlight(
    "public class MyClass { }",
    "csharp",
    new HighlightOptions { ClassPrefix = "syntax-" });

Assert.Equal("""<span class="syntax-keyword">public</span> <span class="syntax-keyword">class</span> <span class="syntax-title">MyClass</span> { }""", html);
```
<!-- endref -->

The prefix may contain ASCII letters, digits, `-` and `_` only; any other character throws an `ArgumentException`. The
sub-scope classes, such as `function_`, and the `language-` classes keep their names. A theme written for the `hljs-`
class names does not apply to another prefix.
