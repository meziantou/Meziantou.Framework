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
| `JavaScript` | ECMAScript | the `u` and `v` flags, `\u{…}`, `[]` and `[^]`, class set operations, no `\A`/`\Z`/`\z`/`\G`, no atomic groups, no extended mode, no inline options |
| `PcrePerl` | PCRE | possessive quantifiers, atomic groups, `\Q…\E`, `\K`, POSIX brackets, recursion and subroutine calls, the `\g` family, callouts, `\h\H\v\V\R\X\N`, `\o{…}`, `\N{U+…}` |
| `PosixExtended` | POSIX | extended regular expressions (ERE) |
| `PosixBasic` | POSIX | basic regular expressions (BRE): `\(…\)` groups, `\{n,m\}` bounds, GNU `\|`, `\+`, `\?`; a bare `(` or `{` is an ordinary character |

Flavors within a family share a parser; `RegexFlavor.Features` records what each one supports.

Where a construct the flavor lacks has an ordinary reading, that is what it gets: `\A` is the letter `A` in JavaScript, and `[a-z-[aeiou]]` in PCRE is the class `[a-z-[aeiou]` followed by a `]`. Where it does not — a grouping construct that flavor simply has no syntax for, such as `(?>…)` in JavaScript — it is reported and then read as a non-capturing group so the body still parses and every character is still accounted for.

### What is covered

Every flavor parses its own grammar into its own node types, not into a pile of literals:

- **`Net`** is complete against Microsoft's regular-expression language reference, balancing groups
  (`(?<c-o>…)`, `(?'c-o'…)`, and the pop-only `(?<-o>…)`) included, with capture numbering that matches the engine.
- **`JavaScript`** covers both Unicode flags. `u` makes a surrogate pair one atom and enables `\u{…}`; `v` adds the
  class set grammar — nested classes, `&&`, `--`, and `\q{…}` string disjunctions.
- **`PcrePerl`** covers recursion and subroutine calls in every spelling (`(?R)`, `(?1)`, `(?&name)`, `(?P>name)`,
  `\g<name>`), the `\g` reference family including relative ones, `(?P=name)`, callouts, backtracking verbs,
  `\Q…\E`, `\h\H\v\V\R\X\N`, `\o{…}`, `\N{U+…}`, `\p{^L}`, and the `J` and `U` options.
- **`PosixExtended`** and **`PosixBasic`** cover bracket expressions, collating elements, and equivalence classes.
  A basic expression's `\(…\)` groups, `\{n,m\}` bounds, and backreferences are all in the tree, as are the GNU
  `\|`, `\+`, and `\?` extensions, and the positional rules that make `^`, `$`, and `*` ordinary characters where
  they cannot be special.

Two notes on how faithful each flavor is, since they were checked against the engines themselves rather than against
a reading of the grammars:

- **`JavaScript` follows whichever grammar the flags select.** Without `u` or `v` the web-compatibility grammar
  applies, so a malformed escape stands for its own letter and `\x4` matches `x4`; with them the grammar is strict and
  the same text is an error. Both directions were checked against V8 over several thousand patterns.
- **`PcrePerl` follows PCRE2 where PCRE2 and Perl differ.** Perl accepts `{2}` with nothing to repeat, `[a-\d]`, and
  `{5,2}`; PCRE2 rejects all three, and so does this.
- **POSIX has no character escapes.** `\x41`, `\cA`, `\n`, and `\k` are the letters `x41`, `cA`, `n`, and `k`, which
  is what the engines see. The shorthand classes it does accept -- `\w`, `\s`, `\b` and their negations -- are the
  GNU extensions.

Java, Python, and RE2/Go are not flavors.

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

`MaxRecursionDepth` bounds how deeply the parser descends. Input that nests beyond it reports `REGEX0200`, and the
remainder is kept as skipped text — which usually means a second diagnostic for the groups that never got closed — so
deeply nested input cannot overflow the stack. A value below one is rejected where it is set.

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
`"😀*"` the quantifier applies to the low surrogate alone. Under the JavaScript `u` flag a pattern is a sequence of code
points instead, so the pair is one atom and `RegexLiteralSyntax.CodePoint` reports it.

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

An edit to a tree from `ParseJavaScriptLiteral` stays a literal: the delimiters and flags are preserved rather than
becoming ordinary characters.

`GetChanges` reports what actually differs between two trees, with the common prefix and suffix trimmed, and
`IsEquivalentTo` compares them structurally, so two patterns that differ only in extended-mode formatting are
equivalent. Trees parsed with different flavors, or with different options, are never equivalent — the same characters
read with and without extended mode are genuinely different trees:

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

Every node stores its own text, so a tree costs memory in proportion to the pattern's length times its depth: a
20,000-character pattern is roughly 16 MB. That is fine for patterns and would not be for documents.

The .NET flavor follows the current .NET engine. The engine changes between releases — .NET 10 rejects
`(?(name)(?n))`, which .NET 11 accepts, and knows fewer Unicode block names — so on an older runtime this parser may
accept a pattern that runtime's own engine would not.
