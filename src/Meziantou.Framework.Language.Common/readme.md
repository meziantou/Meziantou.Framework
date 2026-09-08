# Meziantou.Framework.Language.Common

`Meziantou.Framework.Language.Common` holds the primitives the
`Meziantou.Framework.Language.*` parsers share: source text and its lines, character
spans, text changes, locations, and diagnostics.

It is not useful on its own. It is referenced by
[`Meziantou.Framework.Language.Json`](https://www.nuget.org/packages/Meziantou.Framework.Language.Json/),
[`Meziantou.Framework.Language.Regex`](https://www.nuget.org/packages/Meziantou.Framework.Language.Regex/),
[`Meziantou.Framework.Language.Shell`](https://www.nuget.org/packages/Meziantou.Framework.Language.Shell/), and
[`Meziantou.Framework.Language.Xml`](https://www.nuget.org/packages/Meziantou.Framework.Language.Xml/),
so a span, a text change, or a diagnostic means the same thing whichever of them produced it.

Every type lives in the `Meziantou.Framework.Language` namespace, the parent of each
parser's own namespace, so a file that already has `using Meziantou.Framework.Language.Xml;`
resolves `TextSpan` without a second using directive.

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
