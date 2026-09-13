# Meziantou.Framework.Language.Regex

`Meziantou.Framework.Language.Regex` provides an immutable regular-expression **pattern** concrete syntax tree (CST) with roundtrip-safe parsing, diagnostics, source locations, trivia, and editing helpers.

It is a parser, not an engine: nothing in it matches text. Use `System.Text.RegularExpressions` for that.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

- parse a pattern in the dialect you choose, without rewriting anything
- keep every character, including extended-mode whitespace and comments
- report syntax issues through diagnostics (parsing never throws, whatever the input)
- edit nodes, tokens and trivia, rebuilding only what changed, and serialize back with `ToFullString()`
- walk or rewrite the tree with visitors

The .NET dialect's scanner is ported from [dotnet/runtime](https://github.com/dotnet/runtime)'s own `RegexParser`, so its grammar decisions come from the engine rather than being re-derived. See `THIRD-PARTY-NOTICES.TXT`. A differential test runs every sample and several thousand generated patterns through both this parser and `System.Text.RegularExpressions`, asserting they agree on what is valid.

The other dialects are checked the same way against their engines -- V8 for JavaScript, PCRE2 10.47 for PCRE, and glibc's `regcomp` for POSIX. Several thousand patterns per dialect, with the verdict and the capture groups each engine reported, are replayed by the tests.

## Dialects

| `RegexDialect` | Family | Notes |
| --- | --- | --- |
| `Net` | .NET | balancing groups, character class subtraction, conditionals, `(?#…)`, extended mode |
| `JavaScript` | ECMAScript | the `u` and `v` flags, `\u{…}`, `[]` and `[^]`, class set operations, `(?ims-ims:…)` modifiers, duplicate group names in different alternatives, no `\A`/`\Z`/`\z`/`\G`, no atomic groups, no extended mode |
| `PcrePerl` | PCRE | possessive quantifiers, atomic groups, `\Q…\E`, `\K`, POSIX brackets, recursion and subroutine calls, the `\g` family, callouts, verbs and alpha assertions, branch reset groups, `\h\H\v\V\R\X\N`, `\o{…}`, `\N{U+…}` |
| `PosixExtended` | POSIX | extended regular expressions (ERE), with the GNU `\w`, `\s`, `\b`, `\<`, and `\>` |
| `PosixBasic` | POSIX | basic regular expressions (BRE): `\(…\)` groups, `\{n,m\}` bounds, GNU `\|`, `\+`, `\?`; a bare `(` or `{` is an ordinary character |

Dialects within a family share a parser; `RegexDialect.Features` records what each one supports.

Where a construct the dialect lacks has an ordinary reading, that is what it gets: `\A` is the letter `A` in JavaScript, and `[a-z-[aeiou]]` in PCRE is the class `[a-z-[aeiou]` followed by a `]`. Where it does not — a grouping construct that dialect simply has no syntax for, such as `(?>…)` in JavaScript — it is reported and then read as a non-capturing group so the body still parses and every character is still accounted for.

### What is covered

Every dialect parses its own grammar into its own node types, not into a pile of literals:

- **`Net`** is complete against Microsoft's regular-expression language reference, balancing groups
  (`(?<c-o>…)`, `(?'c-o'…)`, and the pop-only `(?<-o>…)`) included, with capture numbering that matches the engine.
- **`JavaScript`** covers both Unicode flags. `u` makes the pattern a sequence of code points -- a surrogate pair is one
  atom and one endpoint of a range -- and enables `\u{…}`; `v` adds the class set grammar: nested classes, `&&`, `--`,
  and `\q{…}` string disjunctions, with the rules that keep strings out of a negated class. `\p{…}` names are checked
  against the tables of the specification.
- **`PcrePerl`** covers recursion and subroutine calls in every spelling (`(?R)`, `(?1)`, `(?-1)`, `(?&name)`,
  `(?P>name)`, `\g<name>`), the `\g` reference family including relative ones, `(?P=name)`, callouts, backtracking
  verbs, start-of-pattern options, alpha assertions such as `(*plb:…)`, every condition PCRE has (`(?(<name>)…)`,
  `(?(R1)…)`, `(?(DEFINE)…)`, `(?(VERSION>=10.4)…)`, assertions), `\Q…\E` inside and outside a class, the
  `J`, `U`, `^`, `r`, and `aD` options, and loosely matched `\p{…}` names. A lookbehind must have a bounded length.
- **`PosixExtended`** and **`PosixBasic`** cover bracket expressions, collating elements, and equivalence classes, in
  which a backslash is an ordinary character. A basic expression's `\(…\)` groups, `\{n,m\}` bounds, and
  backreferences are all in the tree, as are the GNU `\|`, `\+`, and `\?` extensions, and the positional rules that
  make `^`, `$`, and `*` ordinary characters where they cannot be special.

Two notes on how faithful each dialect is, since they were checked against the engines themselves rather than against
a reading of the grammars:

- **`JavaScript` follows whichever grammar the flags select.** Without `u` or `v` the web-compatibility grammar
  applies, so a malformed escape stands for its own letter and `\x4` matches `x4`; with them the grammar is strict and
  the same text is an error. Both directions were checked against V8 over several thousand patterns.
- **`PcrePerl` follows PCRE2 where PCRE2 and Perl differ.** Perl accepts `{2}` with nothing to repeat, `[a-\d]`, and
  `{5,2}`; PCRE2 rejects all three, and so does this.
- **POSIX has no character escapes.** `\x41`, `\cA`, `\n`, and `\k` are the letters `x41`, `cA`, `n`, and `k`, which
  is what the engines see. The shorthand classes it does accept -- `\w`, `\s`, `\b` and their negations -- are the
  GNU extensions. Where POSIX leaves a construct undefined, the parser does what glibc does: `a**` is fine in an
  extended expression, a `{` always starts an interval, and a backreference must name a group already closed in its
  branch.
- **`JavaScript` follows the specification where V8 does not.** V8 accepts `(?<a>x)(?:y|(?<a>z))`, in which both
  groups can take part in one match; the specification forbids it, and so does this.

Java, Python, and RE2/Go are not dialects.

## Parsing

```csharp
using Meziantou.Framework.Language.Regex;

const string Pattern = """
    (?x)                                   # free-spacing mode
    ^
    (?<ip> \d{1,3} (?: \. \d{1,3} ){3} )   # client address
    \s+ (?<status> [1-5]\d{2} )
    $
    """;

var tree = RegexSyntaxTree.ParseText(Pattern, RegexDialect.Net);

// Nothing is lost: the tree reproduces the input character for character.
Console.WriteLine(tree.GetRoot().ToFullString() == Pattern);   // True

// Invalid input produces diagnostics instead of exceptions.
foreach (var diagnostic in tree.GetDiagnostics())
{
    Console.WriteLine($"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
}
```

A pattern has one root production, so there is no separate entry point for a group, a class, or an atom. Reach them
through the tree instead:

```csharp
var classes = tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>();
var groups = tree.GetRoot().DescendantNodes().OfType<RegexGroupSyntax>();
```

To read a JavaScript literal rather than a bare pattern, delimiters and flags included:

```csharp
var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/a+b/giu");

Console.WriteLine(tree.GetRoot().FlagsToken.Text);   // giu
Console.WriteLine(tree.PatternOptions);             // IgnoreCase, Unicode, Global
Console.WriteLine(tree.GetRoot().ToFullString());   // /a+b/giu
```

## Options

The options an engine is given alongside a pattern change how it is read, so they are part of parsing:

```csharp
var options = new RegexParseOptions(RegexDialect.Net)
{
    PatternOptions = RegexPatternOptions.IgnorePatternWhitespace,
    MaxRecursionDepth = 64,
};

var tree = RegexSyntaxTree.ParseText(pattern, options);
```

`RegexOptionsInterop` converts to and from `System.Text.RegularExpressions.RegexOptions`, keeping only the options that
affect parsing:

```csharp
var patternOptions = RegexOptionsInterop.ToPatternOptions(RegexOptions.IgnoreCase | RegexOptions.Compiled);
Console.WriteLine(patternOptions);   // IgnoreCase
```

`MaxRecursionDepth` bounds how deeply the parser descends. Input that nests beyond it reports `REGEX0200`, and the
remainder is kept as skipped text — which usually means a second diagnostic for the groups that never got closed — so
deeply nested input cannot overflow the stack. A value below one is rejected where it is set.

## Inspecting the tree

Every node exposes its `Kind()`, its `Span` (excluding trivia) and `FullSpan` (including it), its `Parent`, its
`Options`, and the usual traversal methods: `ChildNodes`, `ChildNodesAndTokens`, `DescendantNodes`, `DescendantTokens`,
`DescendantTrivia`, `Ancestors`, plus `FindToken(position)` and `FindNode(span)`. Traversal is in source order.

A pattern body is always an alternation of sequences, even when it has a single branch and no `|`. Keeping the shape
uniform means a consumer never has to handle two spellings of the same thing.

**The bars belong to the list, not to the branches around them.** `Branches` is a separated list: the branches and the
`|` between them share one sequence, so `Count` counts branches and `SeparatorCount` counts bars.

```csharp
var tree = RegexSyntaxTree.ParseText("ab|c", RegexDialect.Net);
var branches = tree.GetRoot().Alternation.Branches;

Console.WriteLine(branches.Count);            // 2
Console.WriteLine(branches.SeparatorCount);   // 1
Console.WriteLine(branches[0].Terms.Count);   // 2
Console.WriteLine(branches.GetSeparator(0));  // |
```

A literal is one atom per UTF-16 code unit, and a quantifier binds the atom in front of it. That matches the engine: in
`"😀*"` the quantifier applies to the low surrogate alone. Under the JavaScript `u` flag a pattern is a sequence of code
points instead, so the pair is one atom and `RegexLiteralSyntax.CodePoint` reports it.

`Options` records what was in effect at each node's first character, which is what makes an inline option setter
readable after the fact:

```csharp
var tree = RegexSyntaxTree.ParseText("a(?i)b", RegexDialect.Net);
var literals = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();

Console.WriteLine(literals[0].Options);   // None
Console.WriteLine(literals[1].Options);   // IgnoreCase
```

Capture groups are numbered the way the engine numbers them. In .NET that is not the order they are written in: named
groups take the first free numbers after every explicitly numbered one. JavaScript and PCRE number every group where it
stands, and a PCRE branch reset group gives each of its branches the same numbers.

```csharp
var tree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.Net);

foreach (var capture in tree.Captures)
{
    Console.WriteLine($"{capture.Number}: {capture.Name}");   // 1: 1 / 2: 2 / 3: x
}

// The same pattern in JavaScript or PCRE: 1: 1 / 2: x / 3: 3
```

## Trivia

A pattern has no trivia unless extended mode is in effect, which `(?x)` can switch on and off part-way through. A
`(?#…)` comment is trivia in every mode.

```csharp
var tree = RegexSyntaxTree.ParseText("a(?#note)b", RegexDialect.Net);

foreach (var comment in tree.GetRoot().DescendantComments())
{
    Console.WriteLine($"{comment.Span.Start}: {comment}");   // 1: (?#note)
}
```

Inside a character class, whitespace and `#` stay literal even under `(?x)`.

## Editing without reparsing

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the pattern is untouched — and the result is the same type you started with, so no cast is needed.

Nothing is carried over onto the replacement, including the trivia in front of the node being replaced. Ask for it
with `WithTriviaFrom` when you want it:

```csharp
var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
var tree = RegexSyntaxTree.ParseText("a   b # keep this\n", options);
var second = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().Last();
var replacement = SyntaxFactory.Literal('z', RegexDialect.Net);

Console.WriteLine(tree.GetRoot().ReplaceNode(second, replacement).ToFullString());
// az # keep this

Console.WriteLine(tree.GetRoot().ReplaceNode(second, replacement.WithTriviaFrom(second)).ToFullString());
// a   z # keep this
```

`ReplaceToken`, `ReplaceTrivia`, `RemoveNode` and the `WithX` method on every slot of every node work the same way.
For text-based edits, use `WithChanges`, which reparses:

```csharp
var tree = RegexSyntaxTree.ParseText("ab+c", RegexDialect.Net);
var updated = tree.WithChanges(new TextChange(new TextSpan(2, 1), "*"));

Console.WriteLine(updated.GetText().Text);   // ab*c
```

An edit to a tree from `ParseJavaScriptLiteral` stays a literal: the delimiters and flags are preserved rather than
becoming ordinary characters.

`GetChanges` reports what actually differs between two trees, with the common prefix and suffix trimmed, and
`IsEquivalentTo` compares them structurally, so two patterns that differ only in extended-mode formatting are
equivalent. Trees parsed with different dialects, or with different options, are never equivalent — the same characters
read with and without extended mode are genuinely different trees:

```csharp
var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
var spaced = RegexSyntaxTree.ParseText("a  b   # note\n", options);
var tight = RegexSyntaxTree.ParseText("ab", options);

Console.WriteLine(spaced.IsEquivalentTo(tight));   // True
```

## Finding a node again after an edit

An annotation is a marker you attach to a node and find again in the tree an edit produced, wherever it ended up:

```csharp
var tree = RegexSyntaxTree.ParseText("a|b", RegexDialect.Net);
var marker = new SyntaxAnnotation();
var branch = tree.GetRoot().Alternation.Branches[1];

var marked = tree.GetRoot().ReplaceNode(branch, branch.WithAdditionalAnnotations(marker));
var edited = marked.ReplaceNode(marked.Alternation.Branches[0], SyntaxFactory.LiteralText("xy", RegexDialect.Net));

var found = edited.GetAnnotatedNodes(marker).Single();
Console.WriteLine(found.ToFullString());   // b
```

## Building trees

`SyntaxFactory` creates nodes programmatically and escapes for the target dialect only when needed:

```csharp
var pattern = SyntaxFactory.Sequence(
    SyntaxFactory.Anchor(RegexAnchorKind.Caret),
    SyntaxFactory.Quantified(SyntaxFactory.ClassEscape('d'), min: 1, max: 3),
    SyntaxFactory.Literal('.', RegexDialect.Net));

Console.WriteLine(pattern.ToFullString());   // ^\d{1,3}\.
```

## Visitors and rewriters

`RegexSyntaxVisitor`, `RegexSyntaxVisitor<TResult>`, and `RegexSyntaxRewriter` cover every node type across all dialects,
so one walker handles any tree. A rewriter descends into every node whatever its type, returns the original instance
when nothing changed, and keeps the exact text of everything it did not touch:

```csharp
sealed class RenameGroup(string oldName, string newName) : RegexSyntaxRewriter
{
    public override SyntaxNode? VisitNamedGroup(RegexNamedGroupSyntax node)
    {
        if (node.Name != oldName)
            return base.VisitNamedGroup(node);

        // WithTriviaFrom keeps whatever stood in front of the old name.
        var renamed = SyntaxFactory.Token(SyntaxKind.NameToken, newName).WithTriviaFrom(node.NameToken);

        return node.WithNameToken(renamed);
    }
}
```

`rewriter.Visit(tree.GetRoot())` returns a new `RegexPatternSyntax`; visiting a node further down scopes the rewrite to
that subtree. `RegexSyntaxWalker` visits a node and everything below it without producing anything, and
`RegexSyntaxVisitor` dispatches on kind without recursing at all.

## Coming from version 3

Version 4 moved the tree onto the same model Roslyn uses, which changed most of the API.

| Version 3 | Version 4 |
| --- | --- |
| `RegexSyntaxKind` | `SyntaxKind` |
| `node.Kind` | `node.Kind()` |
| `RegexSyntaxToken`, `RegexSyntaxTrivia`, `RegexSyntaxNodeOrToken` (classes) | `SyntaxToken`, `SyntaxTrivia`, `SyntaxNodeOrToken` from `Meziantou.Framework.Language` (structs) |
| `tree.Root`, `tree.Text`, `tree.SourceText`, `tree.Diagnostics` | `tree.GetRoot()`, `tree.GetText()`, `tree.GetDiagnostics()` |
| `alternation.BarTokens` | gone — `Branches` is a `SeparatedSyntaxList`, so `Branches.GetSeparator(i)` |
| `new RegexLiteralSyntax(token)` and the other constructors | `SyntaxFactory.Literal(token, options)` and friends |
| `token.WithText(text)` | `SyntaxFactory.Token(kind, text).WithTriviaFrom(token)` |
| `trivia.Text` | `trivia.ToString()` |
| `root.ReplaceNode(...)` re-parsing, and keeping the old node's trivia | `root.ReplaceNode(...)` rebuilding only what changed, and keeping nothing — use `WithTriviaFrom` |
| `RegexSyntaxRewriter` overrides returning `RegexSyntaxNode?` | returning `SyntaxNode?` |
| `RegexSyntaxVisitor.DefaultVisit` recursing | it does nothing; derive from `RegexSyntaxWalker` to walk a tree |

New in version 4: `WithX` on every slot, `SyntaxAnnotation`, `RemoveNode`/`SyntaxRemoveOptions`, `RegexSyntaxWalker`,
`FindToken`/`FindNode`, and structural sharing — an edit keeps every node it did not touch.

## Notes

Replacing a node, token or trivium rebuilds only the spine from it to the root; `WithChanges` reparses, because an
edit expressed as text can change how anything after it reads. A pattern is short enough that either costs nothing
worth saving.

Nodes are shared between the trees an edit produces, so holding several versions of a pattern costs little more than
holding one.

The .NET dialect follows the current .NET engine. The engine changes between releases — .NET 10 rejects
`(?(name)(?n))`, which .NET 11 accepts, and knows fewer Unicode block names — so on an older runtime this parser may
accept a pattern that runtime's own engine would not.
