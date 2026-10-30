---
title: CSS
description: Parse modern CSS, with nesting, selectors, media and container queries and every at-rule, into a lossless tree, edit it, and read its diagnostics.
---

`Meziantou.Framework.Language.Css` parses CSS as browsers read it today into a tree that keeps every character, comments
included. Use it to analyze a style sheet, rename a class, or check the `style` attribute of an HTML element.

```bash
dotnet add package Meziantou.Framework.Language.Css
```

## Parse a style sheet

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = CssSyntaxTree.ParseText("""
    .card {
      color: red; /* the brand color */
      &:hover { color: blue; }
      @media (width >= 600px) { padding: 2rem; }
    }
    """);

Assert.Empty(tree.GetDiagnostics());
Assert.Equal(tree.GetText().Text, tree.GetRoot().ToFullString());
```
<!-- endref -->

The parser reads:

- nesting: declarations and nested rules side by side, `&`, and relative selectors such as `> img`;
- Selectors Level 4, including `:is()`, `:where()`, `:has()`, `:not()`, `:nth-child(An+B of S)`, `:host()`,
  `::part()`, `::slotted()`, `::highlight()` and `::view-transition-group()`;
- Media Queries 4 and 5, with ranges such as `(400px <= width < 800px)`;
- `@supports`, with `selector()`, `font-tech()`, `font-format()` and `at-rule()`;
- container queries: size, `style()` and `scroll-state()`;
- cascade layers and `@scope`;
- every other at-rule: `@starting-style`, `@property`, `@position-try`, `@view-transition`, `@font-feature-values`,
  `@font-palette-values`, `@page` with its margin rules, `@counter-style`, `@keyframes` with timeline ranges,
  `@custom-media` and `@function`.

## Parse a declaration list

The `style` attribute of an HTML element holds declarations, not rules. Set `CssParseOptions.SourceKind` to
`DeclarationList` to parse it:

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_DeclarationList;format=md-fenced-code-block;dedent=true -->
```csharp
// The content of a style attribute is a list of declarations, not a style sheet
var options = new CssParseOptions { SourceKind = CssSourceKind.DeclarationList };
var tree = CssSyntaxTree.ParseText("color: red; margin: 0 auto !important", options);
Assert.Empty(tree.GetDiagnostics());

var declarations = new List<string>();
foreach (var declaration in tree.GetRoot().Declarations)
{
    declarations.Add($"{declaration.Name}: {declaration.GetValueText()} (important: {declaration.IsImportant})");
}

Assert.Equal(["color: red (important: False)", "margin: 0 auto (important: True)"], declarations);
```
<!-- endref -->

`CssParseOptions.MaxDepth`, 128 by default, limits how deeply blocks, functions, selectors and conditions nest. What
nests deeper is kept as skipped text and reported; nesting deeper than the stack can hold is reported too, whatever the
limit says.

## The shape of a tree

A `CssStyleSheetSyntax` holds its `Statements`; `Rules` and `Declarations` filter them. A block holds declarations and
rules side by side, as nesting allows:

| Node | Example | Holds |
| --- | --- | --- |
| `CssQualifiedRuleSyntax` | `a { color: red }` | `Prelude` and `Block`. Its kind is `StyleRule`, `KeyframeRule` or `QualifiedRule` |
| `CssAtRuleSyntax` | `@media print { }`, `@import "a.css";` | `AtKeywordToken`, `Prelude`, then `Block` or `SemicolonToken` |
| `CssDeclarationSyntax` | `color: red !important;` | `Name`, `Values`, `IsImportant`, `GetValueText()` |
| `CssBadDeclarationSyntax` | `foo bar;` in a block | What a browser drops |
| `CssIgnoredTokenSyntax` | A stray `;` | A token CSS ignores |

Each at-rule has a kind of its own, such as `MediaRule`, `LayerRule` or `PageMarginRule`. As for a browser, `@top-left`
is a `PageMarginRule` only inside `@page`; elsewhere, it is an `UnknownAtRule`.

A declaration's value is a list of component values: `CssTokenValueSyntax` for a single token, `CssFunctionSyntax` for
`rgb(…)`, and `CssSimpleBlockSyntax` for `(…)`, `[…]` and `{…}`. There is no grammar for the value of each property, and
`!important` is not part of the values.

### Preludes

The prelude of a rule that has a grammar is parsed with it: a `CssSelectorListSyntax` for a style rule, a
`CssMediaQueryListSyntax` for `@media`, a `CssConditionPreludeSyntax` for `@supports`, a `CssContainerPreludeSyntax`
for `@container`, a `CssLayerPreludeSyntax` for `@layer`, a `CssImportPreludeSyntax` for `@import`, and so on. A
prelude that does not match its grammar is kept as a `CssGenericPreludeSyntax`, with a diagnostic. The prelude of an
unknown at-rule is a `CssGenericPreludeSyntax` too, without a diagnostic.

## Selectors

A selector list holds complex selectors, made of compound selectors and the combinators between them:

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_Selectors;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = (CssQualifiedRuleSyntax)CssSyntaxTree.ParseText("nav > a.active:hover, [href^='https' i] {}").GetRoot().Statements[0];

var parts = new List<string>();
foreach (var selector in rule.Selectors!.Selectors.OfType<CssComplexSelectorSyntax>())
{
    foreach (var part in selector.Parts)
    {
        parts.Add($"{part.CombinatorKind}: {part.Compound}");
    }
}

Assert.Equal(["None: nav", "Child: a.active:hover", "None: [href^='https' i]"], parts);
```
<!-- endref -->

- Whitespace is trivia, so the descendant combinator has no node: a part without a `Combinator` after the first one is a
  descendant of the part before it, and its `CombinatorKind` says so. A comment is not whitespace: `a/**/.b` is one
  compound selector. When you build a selector, put whitespace between two compound selectors that are meant to be
  descendants.
- The argument of a functional pseudo-class depends on it: a selector list for `:is()`, `:where()`, `:not()` and
  `:has()`, a `CssNthArgumentSyntax` for `:nth-child()` and the like, whose `CssAnPlusBSyntax` computes `A` and `B`, and a
  compound selector for `:host()` and `::slotted()`.
- `:is()` and `:where()` are forgiving: a selector of their list that does not parse is a `CssInvalidSelectorSyntax`,
  which matches nothing and leaves the rest of the list alone. A media query that does not parse is a
  `CssInvalidMediaQuerySyntax`, in the same way.
- An unknown pseudo-class or pseudo-element is reported as a warning, since it may only be newer than the parser. One with
  a vendor prefix is not reported. `SyntaxFacts.IsKnownPseudoClass` and `IsKnownPseudoElement` tell which ones the
  parser knows.

## Edit a style sheet

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var root = CssSyntaxTree.ParseText("a { color: red; /* keep */ }").GetRoot();
var value = (CssTokenValueSyntax)root.DescendantNodes().OfType<CssDeclarationSyntax>().Single().Values[0];

var updated = root.ReplaceNode(value, value.WithToken(SyntaxFactory.Identifier("blue").WithTriviaFrom(value.Token)));
Assert.Equal("a { color: blue; /* keep */ }", updated.ToFullString());
```
<!-- endref -->

`SyntaxFactory.Identifier`, `StringToken`, `Hash`, `AtKeyword` and `FunctionToken` escape what CSS requires, and
`Number`, `Percentage` and `Dimension` write numbers that read back as themselves. `Declaration` builds a whole
declaration:

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=SyntaxFactory_Declaration;format=md-fenced-code-block;dedent=true -->
```csharp
var declaration = SyntaxFactory.Declaration("color", SyntaxFactory.CssTokenValue(SyntaxFactory.Identifier("red")));
Assert.Equal("color: red;", declaration.ToFullString());
```
<!-- endref -->

A tree made from an edited root, with `WithRoot` or `CssSyntaxTree.Create`, parses its text again for its diagnostics,
so a node built by code that does not read back as itself is reported.

## Rewrite a style sheet

`CssSyntaxRewriter` has a method per node type, such as `VisitClassSelector` or `VisitDeclaration`. This one renames a
class everywhere:

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxRewriter_RenameClass;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RenameClass : CssSyntaxRewriter
{
    public override SyntaxNode? VisitClassSelector(CssClassSelectorSyntax node)
        => node.Name == "old" ? node.WithNameToken(SyntaxFactory.Identifier("new").WithTriviaFrom(node.NameToken)) : base.VisitClassSelector(node);
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_RewriterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var root = CssSyntaxTree.ParseText(".old, .other .old { color: red; }").GetRoot();

var updated = new RenameClass().Visit(root);
Assert.Equal(".new, .other .new { color: red; }", updated?.ToFullString());
```
<!-- endref -->

`CssSyntaxWalker` visits every node, nested rules included:

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxWalker_DeclarationCounter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class DeclarationCounter : CssSyntaxWalker
{
    public int Count { get; private set; }

    public override void VisitDeclaration(CssDeclarationSyntax node)
    {
        Count++;
        base.VisitDeclaration(node);
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_WalkerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var counter = new DeclarationCounter();
counter.Visit(CssSyntaxTree.ParseText("a { color: red; &:hover { color: blue; } } b { margin: 0 }").GetRoot());

Assert.Equal(3, counter.Count);
```
<!-- endref -->

## Diagnostics

<!-- ref:../../tests/Meziantou.Framework.Language.Css.Tests/CssSyntaxTreeTests.cs;region=CssSyntaxTree_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var messages = new List<string>();
foreach (var diagnostic in CssSyntaxTree.ParseText("a { color: }").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    messages.Add($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
}

Assert.Equal(["CSS0011 (0,4): The declaration 'color' has no value."], messages);
```
<!-- endref -->

An error is something a browser drops: the declaration, the rule, or the construct it is in. A warning is something a
browser accepts, or that may only be newer than the parser.

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
| `CSS0016` | Blocks, functions, selectors and conditions nested deeper than `MaxDepth` |
| `CSS0017` | A misplaced `@import` or `@namespace`, which a browser ignores (warning) |
| `CSS0018` | A `!` that is not followed by `important` |
| `CSS0019` | An unknown at-rule (warning) |
| `CSS0020` to `CSS0029` | Invalid selectors: a missing selector, an unexpected token, whitespace where it is not allowed, simple selectors in the wrong order, an invalid attribute selector, An+B or ID, a leading combinator outside a nested rule, an invalid pseudo-class argument; and an unknown pseudo-class or pseudo-element (warning) |
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

## Not covered

There is no grammar for the value of each property and no cascade. SCSS and Less are not supported: their syntax is
reported as errors, and kept like any other text.
