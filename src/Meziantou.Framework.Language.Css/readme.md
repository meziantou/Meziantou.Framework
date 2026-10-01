# Meziantou.Framework.Language.Css

An immutable CSS syntax tree that keeps every character of the text it was parsed from, comments and whitespace
included, and lets you edit it without reformatting anything you did not touch.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

It reads CSS as browsers read it today:

- Nesting: declarations and nested rules side by side, `&`, and relative selectors such as `> img`.
- Selectors Level 4, including `:is()`, `:where()`, `:has()`, `:not()`, `:nth-child(An+B of S)`, `:host()`, `::part()`, `::slotted()`, `::highlight()`, and `::view-transition-group()`.
- Media Queries 4 and 5, with range syntax such as `(400px <= width < 800px)`.
- `@supports`, with `selector()`, `font-tech()`, `font-format()`, and `at-rule()`.
- Container queries: size, `style()`, and `scroll-state()`.
- Cascade layers and `@scope`.
- Every other at-rule: `@starting-style`, `@property`, `@position-try`, `@view-transition`, `@font-feature-values`, `@font-palette-values`, `@page` with its margin rules, `@counter-style`, `@keyframes` with timeline ranges, `@custom-media`, and `@function`.

Parsing never throws and never gives up. Whatever the text says, the tree reproduces it exactly, and anything wrong
with it is reported through `GetDiagnostics()`.

```csharp
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Css;

var tree = CssSyntaxTree.ParseText("""
    .card {
      color: red; /* the brand color */
      &:hover { color: blue; }
      @media (width >= 600px) { padding: 2rem; }
    }
    """);

Console.WriteLine(tree.GetDiagnostics().Count); // 0
Console.WriteLine(tree.GetRoot().ToFullString() == tree.GetText().Text); // True
```

## The shape of a tree

```
CssStyleSheetSyntax
├── Statements
│   └── CssQualifiedRuleSyntax (StyleRule)
│       ├── Prelude : CssSelectorListSyntax       .card
│       └── Block : CssBlockSyntax
│           ├── OpenBraceToken
│           ├── Statements
│           │   ├── CssDeclarationSyntax            color: red;
│           │   ├── CssQualifiedRuleSyntax          &:hover { ... }
│           │   └── CssAtRuleSyntax (MediaRule)     @media (width >= 600px) { ... }
│           └── CloseBraceToken
└── EndOfFileToken
```

The structure follows CSS Syntax Level 3 as revised for nesting. A block holds declarations and rules side by side.
Something that starts with a name and a colon is a declaration, unless it holds a `{}` block next to other values, as
`a:hover { }` does, in which case it is a nested rule. A custom property is always a declaration.

| Node | Example | Holds |
| --- | --- | --- |
| `CssQualifiedRuleSyntax` | `a { color: red }` | `Prelude`, `Block`. Its kind is `StyleRule`, `KeyframeRule`, or `QualifiedRule` |
| `CssAtRuleSyntax` | `@media print { }`, `@import "a.css";` | `AtKeywordToken`, `Prelude`, then `Block` or `SemicolonToken` |
| `CssDeclarationSyntax` | `color: red !important;` | `NameToken`, `ColonToken`, `Values`, `Important`, `SemicolonToken` |
| `CssBadDeclarationSyntax` | `foo bar;` in a block | what a browser drops, as component values |
| `CssIgnoredTokenSyntax` | a stray `;`, or `<!--` at the top level | a token CSS ignores |

Each at-rule has a kind of its own, such as `MediaRule`, `LayerRule`, or `PageMarginRule`. `@top-left` is a
`PageMarginRule` only inside `@page`, and `@swash` a `FontFeatureValueBlockRule` only inside `@font-feature-values`;
elsewhere they are an `UnknownAtRule`, as for a browser.

A declaration's value is a list of component values: `CssTokenValueSyntax` for a single token, `CssFunctionSyntax`
for `rgb(...)`, and `CssSimpleBlockSyntax` for `(...)`, `[...]`, and `{...}`. There is no grammar for the value of
each property: `Values` is what the declaration says, and `!important` is not part of it.

## Preludes

A rule whose prelude has a grammar is parsed with it:

| Rule | Prelude |
| --- | --- |
| Style rule | `CssSelectorListSyntax`, relative in a nested rule |
| `@media` | `CssMediaQueryListSyntax` |
| `@supports` | `CssConditionPreludeSyntax` |
| `@container` | `CssContainerPreludeSyntax` |
| `@layer` | `CssLayerPreludeSyntax`, with each name a `CssLayerNameSyntax` |
| `@scope` | `CssScopePreludeSyntax` |
| `@import` | `CssImportPreludeSyntax`: the URL, `layer()`, `supports()`, and the media queries |
| `@namespace` | `CssNamespacePreludeSyntax` |
| `@keyframes`, `@property`, `@counter-style`, `@font-palette-values`, `@position-try`, `@charset` | `CssNamePreludeSyntax` |
| `@page` | `CssPageSelectorListSyntax` |
| `@font-feature-values` | `CssNameListSyntax` |
| `@custom-media` | `CssCustomMediaPreludeSyntax` |
| Keyframe rule | `CssKeyframeSelectorListSyntax` |

A prelude that does not match its grammar is kept as a `CssGenericPreludeSyntax` of component values, with a
diagnostic that says why, and `ContainsSkippedText` set, since a browser drops the whole rule. The prelude of an
unknown at-rule, or of `@function`, is a `CssGenericPreludeSyntax` too, without a diagnostic.

Two lists are forgiving: a selector of `:is()` or `:where()` that does not parse is a `CssInvalidSelectorSyntax`, and
a media query that does not parse is a `CssInvalidMediaQuerySyntax`. Each matches nothing, and leaves the rest of its
list and its rule alone.

## Selectors

```csharp
var rule = (CssQualifiedRuleSyntax)CssSyntaxTree.ParseText("nav > a.active:hover, [href^='https' i] {}").GetRoot().Statements[0];

foreach (var selector in rule.Selectors!.Selectors.OfType<CssComplexSelectorSyntax>())
{
    foreach (var part in selector.Parts)
    {
        Console.WriteLine($"{part.CombinatorKind}: {part.Compound}"); // None: nav, Child: a.active:hover, None: [href^='https' i]
    }
}
```

Whitespace is trivia, so the descendant combinator has no node of its own: a part with no `Combinator` after the first
is a descendant of the part before it, and `CombinatorKind` says so. A comment is not whitespace: `a/**/.b` is one
compound selector, as it is for a browser. So when you build a selector, put whitespace between two compound
selectors that are meant to be descendants.

`CssFunctionalPseudoSelectorSyntax.Argument` depends on the pseudo-class: a selector list for `:is()`, `:where()`,
`:not()`, and `:has()`, a `CssNthArgumentSyntax` for `:nth-child()` and the like, whose `CssAnPlusBSyntax` computes
`A` and `B`, a compound selector for `:host()` and `::slotted()`, and a `CssNameListSyntax` for the ones that take
names. An unknown pseudo-class or pseudo-element is parsed and reported as a warning, since it may only be newer than
this parser. One with a vendor prefix is not reported.

## Editing without reformatting

Nodes are immutable: every change returns a new node, and everything you did not change keeps its text.

```csharp
var root = CssSyntaxTree.ParseText("a { color: red; /* keep */ }").GetRoot();
var value = (CssTokenValueSyntax)root.DescendantNodes().OfType<CssDeclarationSyntax>().Single().Values[0];

var updated = root.ReplaceNode(value, value.WithToken(SyntaxFactory.Identifier("blue").WithTriviaFrom(value.Token)));
Console.WriteLine(updated.ToFullString()); // a { color: blue; /* keep */ }
```

`SyntaxFactory` builds tokens and nodes. `Identifier`, `StringToken`, `Hash`, `AtKeyword`, and `FunctionToken` escape
what has to be, `Number`, `Percentage`, and `Dimension` write numbers that read back as themselves, and `Declaration`
builds a whole declaration. A tree made from an edited root, with `WithRoot` or `CssSyntaxTree.Create`, reports the
diagnostics of its text, which it parses again: an edit that builds nodes that do not read back as themselves is
reported then.

## Diagnostics

```csharp
foreach (var diagnostic in CssSyntaxTree.ParseText("a { color: }").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    Console.WriteLine($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
    // CSS0011 (0,4): The declaration 'color' has no value.
}
```

An error is something a browser drops: the declaration, the rule, or the construct it is in. A warning is something a
browser accepts, or that may only be newer than this parser.

| Id | Reported for |
| --- | --- |
| `CSS0001` | An unterminated comment |
| `CSS0002` | An unterminated string, or a line break in a string |
| `CSS0003` | An unquoted URL with a character it cannot hold, or with no closing parenthesis |
| `CSS0004` | A backslash followed by a line break outside a string |
| `CSS0010` | A missing token, such as a closing brace |
| `CSS0011` | Text in a block that is neither a declaration nor a rule, a rule that looks like a custom property, or a declaration with no value |
| `CSS0012` | An unexpected token, such as a closing brace that closes nothing |
| `CSS0013` | A declaration where only rules may be |
| `CSS0014` | A rule or an at-rule where it may not be, such as `@font-face` in a style rule |
| `CSS0015` | An at-rule that requires a block and has none, or the reverse |
| `CSS0016` | Blocks, functions, selectors, and conditions nested deeper than `CssParseOptions.MaxDepth` (128 by default) |
| `CSS0017` | A misplaced `@import` or `@namespace`, which a browser ignores (warning) |
| `CSS0018` | A `!` that is not followed by `important` |
| `CSS0019` | An unknown at-rule (warning) |
| `CSS0020` - `CSS0029` | Invalid selectors: a missing selector, an unexpected token, whitespace where it is not allowed, simple selectors in the wrong order, an invalid attribute selector, An+B, or ID, a leading combinator outside a nested rule, an invalid pseudo-class argument; and an unknown pseudo-class or pseudo-element (warning) |
| `CSS0030` | A selector of a forgiving list that does not parse (warning) |
| `CSS0040` | An at-rule prelude that does not match its grammar |
| `CSS0041` | A media query that does not parse |
| `CSS0042` | `and` and `or` mixed without parentheses |
| `CSS0043` | An invalid range |
| `CSS0044` | An invalid condition |
| `CSS0045` | A condition that is not understood, so is always false (warning) |
| `CSS0046` | An invalid layer name |
| `CSS0047` | An invalid keyframe selector |
| `CSS0048` | An invalid page selector |
| `CSS0050` | An invalid name, such as `@keyframes none` or `@property color` |
| `CSS0051` | `@charset` not written exactly as the first thing in the file (warning) |
| `CSS0052` | A namespace prefix in a selector that no `@namespace` rule declares |

## Options

`CssParseOptions.SourceKind` set to `DeclarationList` parses the content of a block, such as the `style` attribute of
an HTML element, instead of a style sheet. `MaxDepth` limits how deeply things may nest before the rest is kept as
skipped text; nesting that would not fit on the stack of the thread doing the parsing is reported as well, whatever
the limit says.

## Walking a tree

`CssSyntaxWalker` visits every node, and its token and trivia methods when constructed with a deeper
`SyntaxWalkerDepth`. `CssSyntaxRewriter` returns a new tree from the nodes it rewrites, and the nodes it does not
change are kept as they are.

```csharp
sealed class RenameClass : CssSyntaxRewriter
{
    public override SyntaxNode? VisitClassSelector(CssClassSelectorSyntax node)
        => node.Name == "old" ? node.WithNameToken(SyntaxFactory.Identifier("new").WithTriviaFrom(node.NameToken)) : base.VisitClassSelector(node);
}
```

## Not covered

There is no grammar for the value of each property, no cascade, and no SCSS or Less: their syntax is reported as
errors, but kept, like any other text.
