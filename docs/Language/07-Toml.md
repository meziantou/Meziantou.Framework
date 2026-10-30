---
title: TOML
description: Parse TOML 1.0 and 1.1 into a lossless tree, read values by key, edit a file without reformatting it, and read its diagnostics.
---

`Meziantou.Framework.Language.Toml` parses TOML into a tree that keeps every character, so you can change a dependency
in a `Cargo.toml` or a `pyproject.toml` and keep the comments, the quoting and the layout of the file. It reads TOML 1.1,
and TOML 1.0 on request.

```bash
dotnet add package Meziantou.Framework.Language.Toml
```

## Two TOML packages

This package and [`Meziantou.Framework.Toml`](../Toml/01-Introduction.md) both read TOML, for different purposes:

| You want to | Use |
| --- | --- |
| Load a file into your own types, or save them | `Meziantou.Framework.Toml`: see [Serialization](../Toml/04-Serialization/01-Overview.md) |
| Read a document whose shape you don't know, or change a few values and keep the comments | `Meziantou.Framework.Toml`: see [Document model](../Toml/07-DocumentModel.md) |
| Edit a file and keep everything you do not change exactly as written: quotes, number formats, layout | This package |
| Report the problems of a file with their positions, in an editor or a linter | This package, or the syntax tree of `Meziantou.Framework.Toml`: see [Syntax tree, lexer and parser](../Toml/08-SyntaxTreeAndParser.md) |
| Work on TOML with the same model as JSON, XML, INI and the other languages here | This package |
| Check that a file uses only TOML 1.0 | This package, with `TomlVersion.V1_0` |

The two packages have their own types: a node of one cannot be used with the other.

## Parse a document

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxTree_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = TomlSyntaxTree.ParseText("""
    # The server
    [server]
    host = "localhost"
    ports = [ 8000, 8001 ]
    """);

Assert.Empty(tree.GetDiagnostics());

var server = tree.GetRoot().Tables.Single();
var ports = (TomlArraySyntax)server.Properties[1].Value;
Assert.Equal(8000L, ((TomlIntegerSyntax)ports.Elements[0]).Value);
```
<!-- endref -->

## The shape of a tree

A `TomlDocumentSyntax` holds its `Entries` in the order of the text:

- a `TomlTableSyntax` is a table header, `[server]` or `[[products]]`; `IsArrayOfTables` tells them apart;
- a `TomlPropertySyntax` is a key/value pair, `host = "localhost"`, with its `Key` and its `Value`;
- a `TomlSkippedTextSyntax` is a line the parser could not read.

The entries are flat, as in the text: a table header is followed by its key/value pairs rather than being their parent.
`TomlTableSyntax.Properties` gathers the pairs under a header, and `TomlDocumentSyntax.RootProperties` the pairs before
the first header.

A key is a `TomlKeySyntax`, with the parts of a dotted key and the dots between them. Its `Names` are the names without
their quotes and escapes, so `site."google.com"` has the names `site` and `google.com`.

| Value node | Example | `Value` |
| --- | --- | --- |
| `TomlStringSyntax` | `"basic"`, `'literal'`, `"""multi-line"""`, `'''multi-line'''` | `string` |
| `TomlIntegerSyntax` | `42`, `-17`, `1_000`, `0xDEADBEEF`, `0o755`, `0b1101` | `long` |
| `TomlFloatSyntax` | `3.14`, `6.626e-34`, `inf`, `nan` | `double` |
| `TomlBooleanSyntax` | `true`, `false` | `bool` |
| `TomlDateTimeSyntax` | `1979-05-27T07:32:00Z`, `1979-05-27T07:32:00`, `1979-05-27`, `07:32:00` | `DateTimeOffset`, `DateTime`, `DateOnly`, `TimeOnly` |
| `TomlArraySyntax` | `[1, 2, 3]` | `Elements`, a separated list of values |
| `TomlInlineTableSyntax` | `{ x = 1, y = 2 }` | `Properties`, a separated list of key/value pairs |
| `TomlSkippedValueSyntax` | A value that is missing or could not be read | — |

Whitespace and comments up to the end of a line belong to the token that ends the line, and the rest to the next token,
so a comment on its own line goes with the entry below it.

## Read values by key

A table can be written with a header, with dotted keys, or as an inline table, and the three can be mixed. `GetValue`
finds the value of a key whichever way it is written, and `GetKeyValues()` returns every key/value pair of the document
with its full key:

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxTree_ReadValuesByKey;format=md-fenced-code-block;dedent=true -->
```csharp
var root = TomlSyntaxTree.ParseText("""
    [dependencies]
    serde = { version = "1.0" }

    [dependencies.tokio]
    version = "1.2"
    """).GetRoot();

var tokio = (TomlStringSyntax)root.GetValue("dependencies", "tokio", "version")!;
Assert.Equal("1.2", tokio.Value);

var keys = new List<string>();
foreach (var pair in root.GetKeyValues())
{
    keys.Add(string.Join('.', pair.Names));
}

Assert.Equal(["dependencies.serde", "dependencies.serde.version", "dependencies.tokio.version"], keys);
```
<!-- endref -->

Each `TomlKeyValue` has the `Names` of its full key, its `Value`, the `Property` that defines it, the tokens that write
each name in `Parts`, and the `Table` header it is under, which tells apart the tables of an array of tables. A pair whose
value is an inline table comes before the pairs of that table. Values in arrays are not returned, as they have no key of
their own. A list pattern reads the pairs of a table:

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlDocumentSyntax_GetKeyValues;format=md-fenced-code-block;dedent=true -->
```csharp
var document = TomlSyntaxTree.ParseText("""
    [dependencies]
    serde = "1.0"
    tokio = "1.2"
    """).GetRoot();

var dependencies = new List<string>();
foreach (var pair in document.GetKeyValues())
{
    if (pair.Names is ["dependencies", var name] && pair.Value is TomlStringSyntax version)
    {
        dependencies.Add($"{name} {version.Value}");
    }
}

Assert.Equal(["serde 1.0", "tokio 1.2"], dependencies);
```
<!-- endref -->

## Edit values

Replace a value with `WithValue`, which writes it the way TOML requires, and keeps the rest of the line:

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxTree_Edit;format=md-fenced-code-block;dedent=true -->
```csharp
var root = TomlSyntaxTree.ParseText("port = 8080 # the port\n").GetRoot();
var port = (TomlIntegerSyntax)root.RootProperties[0].Value;

var updated = root.ReplaceNode(port, port.WithValue(9090));
Assert.Equal("port = 9090 # the port\n", updated.ToFullString());
```
<!-- endref -->

`SyntaxFactory` builds new nodes. Keys are quoted when they cannot be bare, strings are escaped, and floats always read
back as floats:

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxTree_SyntaxFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var document = SyntaxFactory.TomlDocument(
    SyntaxFactory.TomlTable("server"),
    SyntaxFactory.TomlProperty("host name", SyntaxFactory.TomlString("localhost")),
    SyntaxFactory.TomlProperty(SyntaxFactory.Key("limits", "max"), SyntaxFactory.TomlFloat(1)));

Assert.Equal("""
    [server]
    "host name" = "localhost"
    limits.max = 1.0

    """, document.ToFullString());
```
<!-- endref -->

- Every entry of a document ends its line. `TomlDocument`, `AddEntries`, and the edits that put an entry in front of
  another (`ReplaceNode`, `InsertNodesAfter`, `InsertNodesBefore`, a rewriter) add a line feed to an entry that does not
  end with one.
- `AddElements` and `AddProperties` lay out new items the way the array or the inline table already is: on its line, or
  one per line.
- `SyntaxFactory.ParseValue` reads a single value written as text, the easiest way to get a literal or a multi-line
  string written exactly as you want.
- The factory methods refuse what TOML cannot hold rather than write a document that does not read back: a lone
  surrogate in a string, whitespace other than spaces and tabs, a comment with a control character, or a key/value pair
  whose value starts with a line break.

## TOML versions

`TomlParseOptions.Version` chooses the version of the specification. TOML 1.1, the default, adds line breaks and a
trailing comma in inline tables, the `\e` and `\xHH` escape sequences, and times without seconds. Parsing as TOML 1.0
reports each of them as `TOML0013`, and reads them the same way otherwise:

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxTree_Version;format=md-fenced-code-block;dedent=true -->
```csharp
// A time without seconds is a TOML 1.1 feature
var text = "start = 07:32\n";

var tree = TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = TomlVersion.V1_0 });
Assert.Equal("TOML0013", tree.GetDiagnostics().Single().Id);
```
<!-- endref -->

`TomlParseOptions.MaxDepth`, 128 by default, limits how deeply arrays and inline tables nest. A deeper one is kept whole
as skipped text and reported once. Nesting deeper than the stack can hold is reported the same way, whatever
`MaxDepth` says.

## Diagnostics

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxTree_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var messages = new List<string>();
foreach (var diagnostic in TomlSyntaxTree.ParseText("a = 1\na = 2\n").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    messages.Add($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
}

Assert.Equal(["TOML0020 (1,0): The key 'a' is already defined."], messages);
```
<!-- endref -->

| Id | Reported for |
| --- | --- |
| `TOML0001` | A missing key |
| `TOML0002` | A missing `]`, `]]`, `}`, `=`, or `,`, or a `]` where `]]` was expected |
| `TOML0003` | A missing value |
| `TOML0004` | Something after an entry on its line, where only a comment may follow |
| `TOML0005` | A token that cannot start an entry or a value |
| `TOML0006` | A malformed value: a number, date, or time that breaks the grammar, an integer that does not fit in 64 bits, a float too large for a `double`, a word that is not a value |
| `TOML0007` | A bare key with a character it cannot hold, or a multi-line string used as a key |
| `TOML0008` | An unterminated string |
| `TOML0009` | An invalid escape sequence, or one that is not a Unicode scalar value |
| `TOML0010` | A control character, or whitespace TOML does not allow, such as a non-breaking space |
| `TOML0011` | A carriage return not followed by a line feed |
| `TOML0012` | Arrays and inline tables nested deeper than `MaxDepth` |
| `TOML0013` | A TOML 1.1 feature when parsing as TOML 1.0 |
| `TOML0020` | A key defined twice |
| `TOML0021` | A table defined twice |
| `TOML0022` | An addition to an inline table or an array, which cannot be extended after the fact |
| `TOML0023` | A table header or a dotted key that goes through a value |
| `TOML0024` | A dotted key that adds to a table a header already defined |

The diagnostics from `TOML0020` depend on the whole document rather than on one node: the tree works them out from its
root when it is first asked. A tree made from an edited root, with `WithRoot` or `Create`, parses its text again, so a
duplicate an edit removed is no longer reported, and one it added is.

### Mistakes

TOML is line-oriented, and so is recovery: a key/value pair or a table header that goes wrong never takes the next line
with it. A string without its closing quote ends with its line. An array, or an inline table in TOML 1.1, stops at its
closing bracket, or at a line that can only start the next entry, so a missing `]` is reported once rather than turning
the rest of the document into one array.

### What .NET cannot hold

A few valid TOML values do not fit the .NET types: the year 0 and a leap second (`:60`) are reported as `TOML0006`, an
offset further from UTC than ±14:00 is read as the same instant in UTC, and a fraction of a second is truncated to
seven digits. Line breaks in multi-line strings are read as line feeds. The `Value` of a node in error is a best guess:
check `ContainsDiagnostics` before you rely on it.

## Walk a tree

`TomlSyntaxWalker`, `TomlSyntaxVisitor` and `TomlSyntaxRewriter` have a method per node type, such as `VisitTomlTable`
and `VisitTomlInteger`:

<!-- ref:../../tests/Meziantou.Framework.Language.Toml.Tests/TomlSyntaxTreeTests.cs;region=TomlSyntaxRewriter_DoubleIntegers;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class DoubleIntegers : TomlSyntaxRewriter
{
    public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node) => node.WithValue(node.Value * 2);
}
```
<!-- endref -->
