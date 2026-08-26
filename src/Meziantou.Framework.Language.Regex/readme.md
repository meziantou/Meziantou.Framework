# Meziantou.Framework.Language.Regex

`Meziantou.Framework.Language.Regex` provides an immutable regular-expression **pattern** concrete syntax tree (CST) with roundtrip-safe parsing, diagnostics, source locations, trivia, and editing helpers.

It is a parser, not an engine: nothing in it matches text. Use `System.Text.RegularExpressions` for that.

- parse a pattern in the flavor you choose, without rewriting anything
- keep every character, including extended-mode whitespace and comments
- report syntax issues through diagnostics (parsing never throws, whatever the input)
- edit nodes/tokens/trivia and serialize back with `ToFullString()`
- walk or rewrite the tree with visitors

The .NET flavor's scanner is ported from [dotnet/runtime](https://github.com/dotnet/runtime)'s own `RegexParser`, so its grammar decisions come from the engine rather than being re-derived. See `THIRD-PARTY-NOTICES.TXT`. A differential test runs every sample and several thousand generated patterns through both this parser and `System.Text.RegularExpressions`, asserting they agree on what is valid.

## Flavors

| `RegexFlavor` | Family | Notes |
| --- | --- | --- |
| `Net` | .NET | balancing groups, character class subtraction, conditionals, `(?#…)`, extended mode |
| `JavaScript` | ECMAScript | `u` and `v` flags, no `\A`/`\Z`/`\z`/`\G`, no extended mode, no inline options |
| `PcrePerl` | PCRE | possessive quantifiers, atomic groups, `\Q…\E`, `\K`, POSIX bracket expressions, recursion |
| `PosixExtended` | POSIX | extended regular expressions (ERE) |
| `PosixBasic` | POSIX | basic regular expressions (BRE): no alternation, no `+` or `?`, a bare `(` or `{` is an ordinary character |

Flavors within a family share a parser; `RegexFlavor.Features` records what each one supports. A construct the flavor does not have is not an error, it is simply not that construct: `\A` is the letter `A` in JavaScript, and `[a-z-[aeiou]]` in PCRE is the class `[a-z-[aeiou]` followed by a `]`.

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

var tree = RegexSyntaxTree.ParseText(Pattern, RegexFlavor.Net);

// Nothing is lost: the tree reproduces the input character for character.
Console.WriteLine(tree.Root.ToFullString() == Pattern);   // True

// Invalid input produces diagnostics instead of exceptions.
foreach (var diagnostic in tree.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Id} at {diagnostic.Span}: {diagnostic.Message}");
}
```

A pattern has one root production, so there is no separate entry point for a group, a class, or an atom. Reach them
through the tree instead:

```csharp
var classes = tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>();
var groups = tree.Root.DescendantNodes().OfType<RegexGroupSyntax>();
```

To read a JavaScript literal rather than a bare pattern, delimiters and flags included:

```csharp
var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/a+b/giu");

Console.WriteLine(tree.Root.FlagsToken?.Text);   // giu
Console.WriteLine(tree.PatternOptions);          // IgnoreCase, Unicode, Global
Console.WriteLine(tree.Root.ToFullString());     // /a+b/giu
```

## Options

The options an engine is given alongside a pattern change how it is read, so they are part of parsing:

```csharp
var options = new RegexParseOptions(RegexFlavor.Net)
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

`MaxRecursionDepth` bounds how deeply the parser descends. Input that nests beyond it reports `REGEX0200` and keeps the
remainder as skipped text, so deeply nested input cannot overflow the stack.

## Inspecting the tree

Every node exposes its `Kind`, its `Span` (excluding trivia) and `FullSpan` (including it), its `Parent`, its `Options`,
and the usual traversal methods: `ChildNodes`, `ChildNodesAndTokens`, `DescendantNodes`, `DescendantTokens`,
`DescendantTrivia`, `Ancestors`. Traversal is in source order.

A pattern body is always an alternation of sequences, even when it has a single branch and no `|`. Keeping the shape
uniform means a consumer never has to handle two spellings of the same thing.

```csharp
var tree = RegexSyntaxTree.ParseText("ab|c", RegexFlavor.Net);

Console.WriteLine(tree.Root.Alternation.Branches.Count);           // 2
Console.WriteLine(tree.Root.Alternation.Branches[0].Terms.Count);  // 2
```

A literal is one atom per UTF-16 code unit, and a quantifier binds the atom in front of it. That matches the engine: in
`"😀*"` the quantifier applies to the low surrogate alone.

`Options` records what was in effect at each node's first character, which is what makes an inline option setter
readable after the fact:

```csharp
var tree = RegexSyntaxTree.ParseText("a(?i)b", RegexFlavor.Net);
var literals = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();

Console.WriteLine(literals[0].Options);   // None
Console.WriteLine(literals[1].Options);   // IgnoreCase
```

Capture groups are numbered the way the engine numbers them, which is not the order they are written in: named groups
take the first free numbers after every explicitly numbered one.

```csharp
var tree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexFlavor.Net);

foreach (var capture in tree.Captures)
{
    Console.WriteLine($"{capture.Number}: {capture.Name}");   // 1: 1 / 2: 2 / 3: x
}
```

## Trivia

A pattern has no trivia unless extended mode is in effect, which `(?x)` can switch on and off part-way through. A
`(?#…)` comment is trivia in every mode.

```csharp
var tree = RegexSyntaxTree.ParseText("a(?#note)b", RegexFlavor.Net);

foreach (var comment in tree.Root.DescendantComments())
{
    Console.WriteLine($"{comment.Span.Start}: {comment.Text}");   // 1: (?#note)
}
```

Inside a character class, whitespace and `#` stay literal even under `(?x)`.

## Editing

Edits splice text and reparse, so untouched formatting is preserved exactly. When the replacement carries no leading
trivia of its own, the whitespace in front of the original node is kept:

```csharp
var options = new RegexParseOptions(RegexFlavor.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
var tree = RegexSyntaxTree.ParseText("a   b # keep this\n", options);
var second = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().Last();

var updated = tree.Root.ReplaceNode(second, SyntaxFactory.Literal('z', RegexFlavor.Net));

Console.WriteLine(updated.ToFullString());   // a   z # keep this
```

`ReplaceToken` and `ReplaceTrivia` work the same way. For text-based edits, use `WithChanges`:

```csharp
var tree = RegexSyntaxTree.ParseText("ab+c", RegexFlavor.Net);
var updated = tree.WithChanges(new RegexTextChange(new TextSpan(2, 1), "*"));

Console.WriteLine(updated.Text);   // ab*c
```

`GetChanges` reports what actually differs between two trees, with the common prefix and suffix trimmed, and
`IsEquivalentTo` compares them structurally, so two patterns that differ only in extended-mode formatting are
equivalent:

```csharp
var options = new RegexParseOptions(RegexFlavor.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
var spaced = RegexSyntaxTree.ParseText("a  b   # note\n", options);
var tight = RegexSyntaxTree.ParseText("ab", options);

Console.WriteLine(spaced.IsEquivalentTo(tight));   // True
```

## Building trees

`SyntaxFactory` creates nodes programmatically and escapes for the target flavor only when needed:

```csharp
var pattern = SyntaxFactory.Sequence(
    SyntaxFactory.Anchor(RegexAnchorKind.Caret),
    SyntaxFactory.Quantified(SyntaxFactory.ClassEscape('d'), min: 1, max: 3),
    SyntaxFactory.Literal('.', RegexFlavor.Net));

Console.WriteLine(pattern.ToFullString());   // ^\d{1,3}\.
```

## Visitors and rewriters

`RegexSyntaxVisitor`, `RegexSyntaxVisitor<TResult>`, and `RegexSyntaxRewriter` cover every node type across all flavors,
so one walker handles any tree. A rewriter descends into every node whatever its type, returns the original instance
when nothing changed, and keeps the exact text of everything it did not touch:

```csharp
sealed class RenameGroup(string oldName, string newName) : RegexSyntaxRewriter
{
    public override RegexSyntaxNode? VisitNamedGroup(RegexNamedGroupSyntax node)
    {
        if (node.Name != oldName || node.NameToken is null)
            return base.VisitNamedGroup(node);

        // WithText keeps the token's own trivia, so nothing around the name is lost.
        return new RegexNamedGroupSyntax(
            node.OpenParenToken,
            node.GroupKindToken,
            node.NameToken.WithText(newName),
            node.CloseNameToken,
            node.Alternation,
            node.CloseParenToken,
            node.Number);
    }
}
```

Replaced nodes are spliced into the source and the pattern is reparsed once. `rewriter.Visit(tree.Root)` returns a new
`RegexPatternSyntax`; visiting a node further down scopes the rewrite to that subtree.

## Notes

Every edit reparses the whole pattern. That keeps the model simple and the tree always consistent with its text, and a
pattern is short enough that it costs nothing worth saving.

`PosixBasic` reads `\(`, `\)`, `\{`, and `\}` as escapes rather than as grouping and bounds, so a basic
expression round-trips and reports nothing but its groups are not in the tree as groups.

The .NET flavor follows the current .NET engine. The engine changes between releases — .NET 10 rejects
`(?(name)(?n))`, which .NET 11 accepts, and knows fewer Unicode block names — so on an older runtime this parser may
accept a pattern that runtime's own engine would not.
