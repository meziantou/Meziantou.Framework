# Meziantou.Framework.Language.Json

`Meziantou.Framework.Language.Json` provides an immutable JSON concrete syntax tree (CST) with roundtrip-safe parsing, diagnostics, source locations, trivia (comments/whitespace), and editing helpers.

- parse JSON without reformatting untouched text
- keep comments and trailing commas
- report syntax issues through diagnostics (without throwing on invalid content)
- edit nodes/trivia and serialize back with `ToFullString()`
- evaluate JSONPath expressions directly on syntax nodes

```csharp
using System.Linq;
using Meziantou.Framework.Json;
using Meziantou.Framework.Language.Json;

const string json = """
{
  // comment
  "name": "value",
  "items": [1, 2,],
}
""";

var tree = JsonSyntaxTree.ParseText(json);

// Diagnostics are empty for valid input (including comments/trailing commas)
var diagnostics = tree.Diagnostics;

// Query with JSONPath
var result = JsonPath.Parse("$.items[1]").EvaluateValue(tree);

// Edit a specific node while preserving untouched text
var number = tree.Root.DescendantNodes().OfType<JsonNumberSyntax>().Last();
var updated = tree.Root.ReplaceNode(number, SyntaxFactory.Number("42"));

var updatedJson = updated.ToFullString();
```

You can also use `WithChanges` for text-based incremental edits:

```csharp
var tree = JsonSyntaxTree.ParseText("""{"a":1}""");
var updated = tree.WithChanges(new JsonTextChange(new TextSpan(5, 1), "2"));

Console.WriteLine(updated.Root.ToFullString()); // {"a":2}
```
