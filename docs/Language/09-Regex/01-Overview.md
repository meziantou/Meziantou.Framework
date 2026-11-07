---
title: Overview
description: Parse a regular expression pattern into a lossless tree, find its groups and classes, and read its diagnostics.
---

`Meziantou.Framework.Language.Regex` parses a regular expression pattern into a tree that keeps every character,
including the whitespace and comments of extended mode. Use it to analyze a pattern, show its structure in an editor,
report its mistakes, or rename one of its groups.

It is a parser, not an engine: nothing in it matches text. Use `System.Text.RegularExpressions` for that.

<InstallNuGetPackage Id="Meziantou.Framework.Language.Regex" />

| Page | Covers |
| --- | --- |
| [Dialects](02-Dialects.md) | .NET, JavaScript, PCRE and POSIX patterns, and what each one supports |
| [Options and captures](03-OptionsAndCaptures.md) | The options that change how a pattern is read, extended mode, and the numbering of groups |
| [Editing patterns](04-EditingPatterns.md) | Replacing nodes, building patterns, rewriting, and comparing patterns |

## Parse a pattern

[RegexSyntaxTree.ParseText](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.ParseText) takes the pattern and its dialect. Invalid patterns give diagnostics, not exceptions:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Parsing;format=md-fenced-code-block;dedent=true -->
```csharp
const string Pattern = """
    (?x)                                   # free-spacing mode
    ^
    (?<ip> \d{1,3} (?: \. \d{1,3} ){3} )   # client address
    \s+ (?<status> [1-5]\d{2} )
    $
    """;

var tree = RegexSyntaxTree.ParseText(Pattern, RegexDialect.Net);

// Nothing is lost: the tree reproduces the input character for character.
Assert.Equal(Pattern, tree.GetRoot().ToFullString());
Assert.Empty(tree.GetDiagnostics());

// Invalid input produces diagnostics instead of exceptions.
var invalid = RegexSyntaxTree.ParseText("(a", RegexDialect.Net);
var diagnostic = Assert.Single(invalid.GetDiagnostics());
Assert.Equal("REGEX0002 at (0,0)-(0,2): Unterminated group: expected ')'.", $"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
```
<!-- endref -->

To read a JavaScript regular expression literal, with its slashes and its flags, use [ParseJavaScriptLiteral](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.ParseJavaScriptLiteral). The flags
become the options of the pattern:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_JavaScriptLiteral;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/a+b/giu");

Assert.Equal("giu", tree.GetRoot().FlagsToken.Text);
Assert.Equal(RegexPatternOptions.IgnoreCase | RegexPatternOptions.Unicode | RegexPatternOptions.Global, tree.PatternOptions);
Assert.Equal("/a+b/giu", tree.GetRoot().ToFullString());
```
<!-- endref -->

## Find the parts of a pattern

A pattern has one root, a [RegexPatternSyntax](cref:Meziantou.Framework.Language.Regex.RegexPatternSyntax). Reach its groups, classes and atoms through the tree:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_DescendantNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText(@"(?<year>\d{4})-[01]\d-[0-3]\d", RegexDialect.Net);

var classes = tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>();
var groups = tree.GetRoot().DescendantNodes().OfType<RegexGroupSyntax>();

Assert.Equal(["[01]", "[0-3]"], classes.Select(node => node.ToString()));
Assert.Equal([@"(?<year>\d{4})"], groups.Select(node => node.ToString()));
```
<!-- endref -->

The body of a pattern is always an alternation of sequences, even with a single branch and no `|`, so you never handle
two shapes of the same thing. `Alternation.Branches` is a separated list: the branches and the `|` between them share one
sequence.

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Branches;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("ab|c", RegexDialect.Net);
var branches = tree.GetRoot().Alternation.Branches;

Assert.Equal(2, branches.Count);
Assert.Equal(1, branches.SeparatorCount);
Assert.Equal(2, branches[0].Terms.Count);
Assert.Equal("|", branches.GetSeparator(0).ToString());
```
<!-- endref -->

Every dialect parses its own syntax into its own node types: a group is a [RegexGroupSyntax](cref:Meziantou.Framework.Language.Regex.RegexGroupSyntax), a named group a
[RegexNamedGroupSyntax](cref:Meziantou.Framework.Language.Regex.RegexNamedGroupSyntax), a lookaround a [RegexLookaroundSyntax](cref:Meziantou.Framework.Language.Regex.RegexLookaroundSyntax), a quantified atom a [RegexQuantifiedSyntax](cref:Meziantou.Framework.Language.Regex.RegexQuantifiedSyntax), and so on.

A literal is one atom per UTF-16 code unit, and a quantifier applies to the atom in front of it, as the engine reads it:
in `😀*`, the quantifier applies to the low surrogate alone. With the JavaScript `u` or `v` flag, a pattern is a sequence
of code points instead: the pair is one atom, and [RegexLiteralSyntax.CodePoint](cref:Meziantou.Framework.Language.Regex.RegexLiteralSyntax.CodePoint) gives it.

## Diagnostics

The identifiers are grouped by what they are about:

| Ids | About |
| --- | --- |
| `REGEX0001` to `REGEX0019` | Brackets, parentheses, quantifiers, escapes and group names, in every dialect |
| `REGEX0030` to `REGEX0035` | Backreferences, Unicode properties and duplicate group names |
| `REGEX0050` to `REGEX0056` | Constructs only .NET has, such as class subtraction and conditionals |
| `REGEX0070` to `REGEX0075` | JavaScript: class set operations, class strings and modifiers |
| `REGEX0090` to `REGEX0093` | PCRE: backtracking verbs, callouts, lookbehinds and conditions |
| `REGEX0110`, `REGEX0111` | POSIX: bracket expressions and intervals |
| `REGEX0200` and up | The parser itself: nesting deeper than [MaxRecursionDepth](cref:Meziantou.Framework.Language.Regex.RegexParseOptions.MaxRecursionDepth), and the flags and delimiters of a JavaScript literal |

The message says what was found; test the [Id](cref:Meziantou.Framework.Language.Diagnostic.Id) in code. See [Diagnostics](../03-Concepts/05-Diagnostics.md).

## Walk a pattern

[RegexSyntaxWalker](cref:Meziantou.Framework.Language.Regex.RegexSyntaxWalker), [RegexSyntaxVisitor](cref:Meziantou.Framework.Language.Regex.RegexSyntaxVisitor), [RegexSyntaxVisitor\<TResult\>](cref:Meziantou.Framework.Language.Regex.RegexSyntaxVisitor{TResult}) and [RegexSyntaxRewriter](cref:Meziantou.Framework.Language.Regex.RegexSyntaxRewriter) cover the node types
of every dialect, so one walker handles any pattern:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxVisitorTests.cs;region=RegexSyntaxWalker_LiteralCounter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class LiteralCounter : RegexSyntaxWalker
{
    public int Count { get; private set; }

    public override void VisitLiteral(RegexLiteralSyntax node)
    {
        Count++;
        base.VisitLiteral(node);
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxVisitorTests.cs;region=RegexSyntaxWalker_LiteralCounterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("ab|c", RegexDialect.Net);
var counter = new LiteralCounter();

counter.Visit(tree.GetRoot());

Assert.Equal(3, counter.Count);
```
<!-- endref -->
