# Meziantou.Framework.Language.Shell

`Meziantou.Framework.Language.Shell` provides an immutable shell script concrete syntax tree (CST) with roundtrip-safe parsing, diagnostics, source locations, trivia (comments/whitespace), and editing helpers.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

- parse a script in the dialect you choose, without reformatting untouched text
- keep every character, including comments, blank lines, and line continuations
- report syntax issues through diagnostics (parsing never throws, whatever the input)
- edit nodes, tokens and trivia, rebuilding only what changed, and serialize back with `ToFullString()`
- walk or rewrite the tree with visitors

## Dialects

| `ShellDialect` | Family | Notes |
| --- | --- | --- |
| `Sh` | POSIX | strict POSIX baseline |
| `Bash` | POSIX | `[[ ]]`, `(( ))`, `<<<`, arrays, `function`, `<(…)`, `coproc`, `select`, arithmetic `**` |
| `Zsh` | POSIX | the bash set plus `foreach`/`end`, `repeat`, `always`, anonymous functions, `=(…)`, glob qualifiers, and brace groups that close without a separator |
| `PowerShell` | PowerShell | Windows PowerShell 5.1 |
| `PowerShellCore` | PowerShell | pwsh 7+: `&&`/`\|\|`, ternary `? :`, `??`/`??=`, `clean` blocks |
| `Cmd` | Cmd | cmd.exe batch |

Dialects within a family share a parser; `ShellDialect.Features` records what each one supports. POSIX defines `$((1+2))`, so every dialect in the family parses it as an arithmetic expansion, while the `((1+2))` arithmetic command is bash and zsh only and stays plain text in sh.

## Parsing

```csharp
using Meziantou.Framework.Language.Shell;

const string Script = """
    # deploy the app
    set -euo pipefail

    for target in web api; do
      if [[ -d "src/$target" ]]; then
        dotnet publish "src/$target" -c Release | tee "logs/$target.log"
      fi
    done
    """;

var tree = ShellSyntaxTree.ParseText(Script, ShellDialect.Bash);

// Nothing is lost: the tree reproduces the input byte for byte.
Console.WriteLine(tree.GetRoot().ToFullString() == Script); // True

// Invalid input produces diagnostics instead of exceptions.
foreach (var diagnostic in tree.GetDiagnostics())
{
    Console.WriteLine($"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
}
```

### Diagnostics

A syntax error is reported and parsing carries on: the parser resynchronizes at the next statement, or at the keyword
that closes the construct it is in, so one mistake does not hide the rest of the script. The ids are shared by every
dialect; the message says what was found.

| Id | Meaning |
| --- | --- |
| `SHELL0001` | A command was expected, as in `a \|` at the end of a line or `if true; then fi` outside zsh |
| `SHELL0002` | An unexpected token, such as `fi` with no `if`, a command right after `}` on the same line, or `;;` outside `case` |
| `SHELL0003` | An unterminated string |
| `SHELL0004` | A redirection with no target |
| `SHELL0005`–`SHELL0007`, `SHELL0010` | An unterminated `${`, `$(` or backquote, `$((` or `((`, `[[` |
| `SHELL0008`, `SHELL0009` | A missing `)` or `}` |
| `SHELL0011` | A here-document with no closing delimiter, a warning because the shells read it to the end of the input |
| `SHELL0012`, `SHELL0013` | A missing keyword or character, a missing name |
| `SHELL0014` | A function body that is not a compound command, outside zsh |
| `SHELL0015` | An invalid `[[ ]]` expression |
| `SHELL0020`–`SHELL0027` | PowerShell: block comments, member names, here-strings, missing expressions, `using` placement, reserved keywords, missing parameter arguments, invalid assignment targets |
| `SHELL0040`, `SHELL0041` | cmd: an incomplete `if` condition, a missing comparison operator |
| `SHELL0100`, `SHELL0101` | Nesting past `MaxRecursionDepth`; content after the statement `ParseCommand` read |

To read a single command rather than a whole script:

```csharp
var command = (ShellCommandSyntax)ShellSyntaxTree.ParseCommand("git commit -m 'wip'", ShellDialect.Bash);

Console.WriteLine(command.NameValue);                       // git
Console.WriteLine(command.Arguments[2].Value);              // wip
```

`ParseCommand` is the entry point for a single command; there is no separate expression entry point, because what a
shell expression *is* differs per dialect. Expressions are reached through the nodes that contain them:
`PosixArithmeticExpansionSyntax.Expression` for `$(( ))`, `PosixDelimitedExpressionStatementSyntax.Expression` for
`(( ))` and `[[ ]]`, and `PowerShellExpressionStatementSyntax.Expression` for a PowerShell expression statement.

```csharp
var command = (ShellCommandSyntax)ShellSyntaxTree.ParseCommand("ls /root", ShellDialect.Bash);
var arithmetic = ShellSyntaxTree.ParseText("echo $((1 + 2 * 3))", ShellDialect.Bash)
    .GetRoot().DescendantNodes().OfType<PosixArithmeticExpansionSyntax>().Single();

// 1 + (2 * 3): precedence is in the tree, not left to the caller.
var binary = (ShellBinaryExpressionSyntax)arithmetic.Expression;
Console.WriteLine(binary.OperatorText);                     // +
Console.WriteLine(binary.Right is ShellBinaryExpressionSyntax); // True
```

## Inspecting the tree

Every node exposes its `Kind()`, its `Span` (excluding trivia) and `FullSpan` (including it), its `Parent`, and the usual traversal methods: `ChildNodes()`, `ChildNodesAndTokens()`, `DescendantNodes()`, `DescendantTokens()`, `DescendantTrivia()`, `Ancestors()`, plus `FindToken(position)` and `FindNode(span)`. Traversal is in source order.

**The separators belong to the list, not to what they follow.** A statement list, a pipeline, and a command list are
separated lists: the statements and the `;`, `|` or `&&` between them share one sequence, so `Count` counts statements
and `SeparatorCount` counts separators.

Comments are trivia, so they never interrupt the node structure but still round-trip:

```csharp
foreach (var comment in tree.GetRoot().DescendantComments())
{
    Console.WriteLine($"{comment.Span.Start}: {comment}");
}
```

## Editing without reparsing

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the script is untouched — and the result is the same type you started with, so no cast is needed.

Nothing is carried over onto the replacement, including the trivia in front of the node being replaced. Ask for it
with `WithTriviaFrom` when you want it:

```csharp
var tree = ShellSyntaxTree.ParseText("echo   old    # keep this", ShellDialect.Bash);
var command = (ShellCommandSyntax)tree.GetRoot().Statements.Statements[0];
var argument = command.Arguments[0];

var updated = tree.GetRoot().ReplaceNode(argument, SyntaxFactory.Word("new", ShellDialect.Bash).WithTriviaFrom(argument));

Console.WriteLine(updated.ToFullString()); // echo   new    # keep this
```

`ReplaceToken`, `ReplaceTrivia`, `RemoveNode` and the `WithX` method on every slot of every node work the same way.
For text-based edits, use `WithChanges`, which reparses:

```csharp
var tree = ShellSyntaxTree.ParseText("echo old", ShellDialect.Bash);
var updated = tree.WithChanges(new TextChange(new TextSpan(5, 3), "new"));

Console.WriteLine(updated.GetRoot().ToFullString()); // echo new
```

`GetChanges` reports what actually differs between two trees, with the common prefix and suffix trimmed, and
`IsEquivalentTo` compares them structurally, so two scripts that differ only in whitespace or comments are equivalent:

```csharp
var a = ShellSyntaxTree.ParseText("echo   a  # note", ShellDialect.Bash);
var b = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash);

Console.WriteLine(a.IsEquivalentTo(b)); // True
```

## Finding a node again after an edit

An annotation is a marker you attach to a node and find again in the tree an edit produced, wherever it ended up:

```csharp
var marker = new SyntaxAnnotation();
var marked = root.ReplaceNode(command, command.WithAdditionalAnnotations(marker));
var edited = marked.ReplaceNode(/* something else entirely */);

var found = edited.GetAnnotatedNodes(marker).Single();
```

## Building trees

`SyntaxFactory` creates nodes programmatically and quotes for the target dialect only when needed:

```csharp
var command = SyntaxFactory.Command(ShellDialect.Bash, "echo", "two words", "plain");

Console.WriteLine(command.ToFullString()); // echo 'two words' plain
```

## Visitors and rewriters

`ShellSyntaxVisitor`, `ShellSyntaxVisitor<TResult>`, and `ShellSyntaxRewriter` cover every node type across all dialects, so one walker handles any tree. A rewriter descends into every node whatever its type, returns the original instance when nothing changed, and keeps the exact text of everything it did not touch:

```csharp
sealed class RenameCommand(string oldName, string newName) : ShellSyntaxRewriter
{
    public override SyntaxNode? VisitCommand(ShellCommandSyntax node)
    {
        if (node.NameValue != oldName || node.Name is null)
            return base.VisitCommand(node);

        // WithText keeps the word's own leading trivia, so the comment and indentation in front of the
        // command are not lost. A node built from scratch carries no trivia and would drop them.
        var renamed = node.Name.WithText(newName);

        return node.WithElements(new SyntaxList<ShellSyntaxNode>(
            node.Elements.Select(child => ReferenceEquals(child, node.Name) ? renamed : child)));
    }
}
```

`rewriter.Visit(tree.GetRoot())` returns a new `ShellScriptSyntax`; visiting a node further down scopes the rewrite to
that subtree. `ShellSyntaxWalker` visits a node and everything below it without producing anything, and
`ShellSyntaxVisitor` dispatches on kind without recursing at all.

## Parse options

```csharp
var options = new ShellParseOptions(ShellDialect.Bash) { MaxRecursionDepth = 64 };
var tree = ShellSyntaxTree.ParseText(script, options);
```

`MaxRecursionDepth` bounds how deeply the parser descends. Input that nests beyond it reports `SHELL0100` and keeps the remainder as skipped text, so deeply nested input cannot overflow the stack.

The depth limit bounds nesting, not length. An operator or member chain such as `$x.a.b.c...` is built by a loop rather than by recursion, so it is accepted at any length and produces a tree as deep as the chain is long. Building, walking, and editing such a tree uses no recursion either.

## Coming from version 2

Version 3 moved the tree onto the same model Roslyn uses, which changed most of the API.

| Version 2 | Version 3 |
| --- | --- |
| `ShellSyntaxKind` | `SyntaxKind` |
| `node.Kind` | `node.Kind()` |
| `ShellSyntaxToken`, `ShellSyntaxTrivia`, `ShellSyntaxNodeOrToken` (classes) | `SyntaxToken`, `SyntaxTrivia`, `SyntaxNodeOrToken` from `Meziantou.Framework.Language` (structs) |
| `tree.Root`, `tree.Text`, `tree.SourceText`, `tree.Diagnostics` | `tree.GetRoot()`, `tree.GetText()`, `tree.GetDiagnostics()` |
| `node.ChildNodes` (a property) | `node.ChildNodes()`, or the node's own named slots |
| `command.ChildNodes` / `WithChildNodes` | `command.Elements` / `WithElements` |
| `list.SeparatorTokens` alongside the nodes | one `SeparatedSyntaxList`; the named property still reads the separators |
| `token.Text` on an absent optional token being `null` | the default token, whose text is `""` — ask `token.IsPresent()` |
| `trivia.Text` | `trivia.ToString()` |
| `root.ReplaceNode(...)` re-parsing, and keeping the old node's trivia | `root.ReplaceNode(...)` rebuilding only what changed, and keeping nothing — use `WithTriviaFrom` |
| `root.ReplaceNode(foreign, …)` quietly doing nothing | it throws, because the node is not in the tree |
| `ShellSyntaxRewriter` overrides returning `ShellSyntaxNode?` | returning `SyntaxNode?` |
| `ShellSyntaxVisitor.DefaultVisit` recursing | it does nothing; derive from `ShellSyntaxWalker` to walk a tree |
| `redirection.HereDocument` set by the parser | derived from the tree, so it survives an edit |

New in version 3: `WithX` on every slot, `SyntaxAnnotation`, `RemoveNode`/`SyntaxRemoveOptions`, `ShellSyntaxWalker`,
`FindToken`/`FindNode`, and structural sharing — an edit keeps every node it did not touch.

## Notes

Replacing a node, token or trivium rebuilds only the spine from it to the root; `WithChanges` reparses, because an
edit expressed as text can change how everything after it reads.

Nodes are shared between the trees an edit produces, so holding several versions of a script costs little more than
holding one.
