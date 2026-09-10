# Meziantou.Framework.Language

`Meziantou.Framework.Language` holds what the `Meziantou.Framework.Language.*` parsers
share: source text and its lines, character spans, text changes, locations, diagnostics,
and the syntax tree itself.

It is not useful on its own. It is referenced by
[`Meziantou.Framework.Language.Json`](https://www.nuget.org/packages/Meziantou.Framework.Language.Json/),
[`Meziantou.Framework.Language.Regex`](https://www.nuget.org/packages/Meziantou.Framework.Language.Regex/),
[`Meziantou.Framework.Language.Shell`](https://www.nuget.org/packages/Meziantou.Framework.Language.Shell/), and
[`Meziantou.Framework.Language.Xml`](https://www.nuget.org/packages/Meziantou.Framework.Language.Xml/),
so a span, a text change, or a diagnostic means the same thing whichever of them produced it.

Every type lives in the `Meziantou.Framework.Language` namespace, the parent of each
parser's own namespace, so a file that already has `using Meziantou.Framework.Language.Xml;`
resolves `TextSpan` without a second using directive.

## Syntax trees

The tree types here are modelled on Roslyn's, and are split the same way.

A **green** node is immutable and knows its *width*, never its position, so one instance can
appear at any offset in any number of trees. A **red** node — `SyntaxNode` and the
`SyntaxToken` / `SyntaxTrivia` structs — adds a parent and an absolute position, and is
created on demand as a tree is walked.

That split is what makes editing cheap and honest. Replacing a node rebuilds only the path
from it up to the root; every other subtree is carried over by reference, so the parts that
did not change stay the very same nodes:

```csharp
var updated = root.ReplaceNode(oldNode, newNode);

// The sibling was not touched, so it was not rebuilt.
untouchedSibling.IsIncrementallyIdenticalTo(updatedSibling); // true
```

`ReplaceNode` returns the type it was given, so editing a document gives back a document. `RemoveNode` does too, and
takes `SyntaxRemoveOptions` saying what to do with the trivia around what it takes out — the comment in front of a
node would otherwise disappear with it, silently.

Other things the split buys: `SeparatedSyntaxList<T>`, where the elements and the separators
between them share one sequence (which is why a trailing comma needs no special case);
`SyntaxAnnotation`, a marker you can attach to a node and find again after an edit; and
diagnostics that are stored relative to the node that owns them, so a node carries its
errors wherever it ends up.

The green types are internal. Building a parser on this package is not supported — the four
`Meziantou.Framework.Language.*` languages are the intended consumers, exactly as Roslyn
keeps its own green layer to itself.

## Diagnostics and locations

A diagnostic carries a `Location`, which pairs the character range with the text it indexes
into, so it can report line and character positions:

```csharp
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Xml;

var tree = XmlSyntaxTree.ParseText("<root>\n  <item>\n</root>");

foreach (var diagnostic in tree.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");

    var lineSpan = diagnostic.Location.GetLineSpan();
    Console.WriteLine(lineSpan.Start.Line);      // zero-based
    Console.WriteLine(lineSpan.Start.Character); // zero-based
}
```

A diagnostic produced by a parser is always bound to the tree's own text, so
`diagnostic.Location.SourceText` is the same instance as `tree.SourceText`. A `Location`
you build yourself may leave the text out, in which case `GetLineSpan()` returns `default`.

## Source text and edits

```csharp
var source = SourceText.From("<root/>");

var updated = source.WithChanges([new TextChange(new TextSpan(1, 4), "item")]);
Console.WriteLine(updated.Text); // <item/>

var line = source.GetLine(position: 3);
Console.WriteLine(line.LineNumber);
```

`WithChanges` applies changes from the end of the text backwards, so the spans of the
changes that come earlier in the text stay valid regardless of the order they are passed
in. A change reaching past the end of the text is ignored.
