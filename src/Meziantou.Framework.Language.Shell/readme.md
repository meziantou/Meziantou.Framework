# Meziantou.Framework.Language.Shell

`Meziantou.Framework.Language.Shell` provides an immutable shell script concrete syntax tree (CST) with roundtrip-safe parsing, diagnostics, source locations, trivia (comments/whitespace), and editing helpers.

- parse a script in the dialect you choose, without reformatting untouched text
- keep every character, including comments, blank lines, and line continuations
- report syntax issues through diagnostics (parsing never throws, whatever the input)
- edit nodes/tokens/trivia and serialize back with `ToFullString()`
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
Console.WriteLine(tree.Root.ToFullString() == Script); // True

// Invalid input produces diagnostics instead of exceptions.
foreach (var diagnostic in tree.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Id} at {diagnostic.Span}: {diagnostic.Message}");
}
```

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
    .Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>().Single();

// 1 + (2 * 3): precedence is in the tree, not left to the caller.
var binary = (ShellBinaryExpressionSyntax)arithmetic.Expression;
Console.WriteLine(binary.OperatorText);                     // +
Console.WriteLine(binary.Right is ShellBinaryExpressionSyntax); // True
```

## Inspecting the tree

Every node exposes its `Kind`, its `Span` (excluding trivia) and `FullSpan` (including it), its `Parent`, and the usual traversal methods: `ChildNodes`, `ChildNodesAndTokens`, `DescendantNodes`, `DescendantTokens`, `DescendantTrivia`, `Ancestors`. Traversal is in source order.

Comments are trivia, so they never interrupt the node structure but still round-trip:

```csharp
foreach (var comment in tree.Root.DescendantComments())
{
    Console.WriteLine($"{comment.Span.Start}: {comment.Text}");
}
```

## Editing

Edits splice text and reparse, so untouched formatting is preserved exactly. When the replacement carries no leading trivia of its own, the whitespace in front of the original node is kept:

```csharp
var tree = ShellSyntaxTree.ParseText("echo   old    # keep this", ShellDialect.Bash);
var command = (ShellCommandSyntax)tree.Root.Statements.Statements[0];

var updated = tree.Root.ReplaceNode(command.Arguments[0], SyntaxFactory.Word("new", ShellDialect.Bash));

Console.WriteLine(updated.ToFullString()); // echo   new    # keep this
```

`ReplaceToken` and `ReplaceTrivia` work the same way. For text-based edits, use `WithChanges`:

```csharp
var tree = ShellSyntaxTree.ParseText("echo old", ShellDialect.Bash);
var updated = tree.WithChanges(new ShellTextChange(new TextSpan(5, 3), "new"));

Console.WriteLine(updated.Root.ToFullString()); // echo new
```

`GetChanges` reports what actually differs between two trees, with the common prefix and suffix trimmed, and
`IsEquivalentTo` compares them structurally, so two scripts that differ only in whitespace or comments are equivalent:

```csharp
var a = ShellSyntaxTree.ParseText("echo   a  # note", ShellDialect.Bash);
var b = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash);

Console.WriteLine(a.IsEquivalentTo(b)); // True
```

Every edit reparses the whole script. That keeps the model simple and the tree always consistent with its text;
parsing runs at roughly 0.4 microseconds per character, so a typical script reparses in well under a millisecond.

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
    public override ShellSyntaxNode? VisitCommand(ShellCommandSyntax node)
    {
        if (node.NameValue != oldName || node.Name is null)
            return base.VisitCommand(node);

        // WithText keeps the original leading trivia, so the comment and indentation in front of the
        // command are not lost. A node built from scratch carries no trivia and would drop them.
        var renamed = node.Name.WithText(newName);

        return node.WithChildNodes(node.ChildNodes.Select(child => ReferenceEquals(child, node.Name) ? renamed : child));
    }
}
```

`ReplaceNode` applies the same rule for you: when the replacement has no leading trivia of its own, the trivia in front of the node being replaced is kept. The rewriter follows that rule too.

Replaced nodes are spliced into the source and the script is reparsed once. `rewriter.Visit(tree.Root)` returns a
new `ShellScriptSyntax`; visiting a node further down scopes the rewrite to that subtree and returns the node that
took its place.

## Parse options

```csharp
var options = new ShellParseOptions(ShellDialect.Bash) { MaxRecursionDepth = 64 };
var tree = ShellSyntaxTree.ParseText(script, options);
```

`MaxRecursionDepth` bounds how deeply the parser descends. Input that nests beyond it reports `SHELL0100` and keeps the remainder as skipped text, so deeply nested input cannot overflow the stack.

The depth limit bounds nesting, not length. An operator or member chain such as `$x.a.b.c...` is built by a loop rather than by recursion, so it is accepted at any length and produces a tree as deep as the chain is long. Building, walking, and editing such a tree uses no recursion either, but every node holds its own text, so a chain of many thousands of links costs memory in proportion to its length times its depth.
