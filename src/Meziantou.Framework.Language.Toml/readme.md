# Meziantou.Framework.Language.Toml

An immutable TOML syntax tree that keeps every character of the text it was parsed from (comments, whitespace,
quoting style) and lets you edit it without reformatting anything you did not touch. It reads TOML 1.1, the current
version of the specification, and TOML 1.0 on request, and passes the whole
[toml-test](https://github.com/toml-lang/toml-test) conformance suite for both.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

Parsing never throws and never gives up. Whatever the text says, the tree reproduces it exactly, and anything wrong
with it is reported through `GetDiagnostics()`: a missing bracket as much as a key defined twice.

```csharp
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Toml;

var tree = TomlSyntaxTree.ParseText("""
    # The server
    [server]
    host = "localhost"
    ports = [ 8000, 8001 ]
    """);

Console.WriteLine(tree.GetDiagnostics().Count); // 0

var server = tree.GetRoot().Tables.Single();
var ports = (TomlArraySyntax)server.Properties[1].Value;
Console.WriteLine(((TomlIntegerSyntax)ports.Elements[0]).Value); // 8000
```

## The shape of a tree

```
TomlDocumentSyntax
├── Entries : SyntaxList<TomlEntrySyntax>     (flat, in source order)
│   ├── TomlTableSyntax        →  OpenBracketToken, Key, CloseBracketToken     [server], or [[products]]
│   ├── TomlPropertySyntax     →  Key, EqualsToken, Value                      host = "localhost"
│   └── TomlSkippedTextSyntax  →  Tokens                                       a line that could not be read
└── EndOfFileToken
```

The entries are flat, as they are in the text: a table header is followed by the key/value pairs under it rather than
being their parent. `TomlTableSyntax.Properties` gathers the pairs under a header, `TomlDocumentSyntax.RootProperties`
the ones before the first header, and `TomlTableSyntax.IsArrayOfTables` tells `[a]` from `[[a]]`.

A key is a `TomlKeySyntax`: the parts of a dotted key and the dots between them. `Names` has the name of each part with
its quotes removed and its escape sequences resolved, so `site."google.com"` has the names `site` and `google.com`.

A value is one of:

| Node | Example | `Value` |
| --- | --- | --- |
| `TomlStringSyntax` | `"basic"`, `'literal'`, `"""multi-line"""`, `'''multi-line'''` | `string` |
| `TomlIntegerSyntax` | `42`, `-17`, `1_000`, `0xDEADBEEF`, `0o755`, `0b1101` | `long` |
| `TomlFloatSyntax` | `3.14`, `6.626e-34`, `inf`, `nan` | `double` |
| `TomlBooleanSyntax` | `true`, `false` | `bool` |
| `TomlDateTimeSyntax` | `1979-05-27T07:32:00Z` | `DateTimeOffset` |
| | `1979-05-27T07:32:00` | `DateTime` |
| | `1979-05-27` | `DateOnly` |
| | `07:32:00` | `TimeOnly` |
| `TomlArraySyntax` | `[1, 2, 3]` | `Elements`, a separated list of values |
| `TomlInlineTableSyntax` | `{ x = 1, y = 2 }` | `Properties`, a separated list of key/value pairs |
| `TomlSkippedValueSyntax` | a value that could not be read, or that is missing | `Tokens` |

The commas of an array or an inline table belong to the list, not to what they follow: `Count` counts elements and
`SeparatorCount` counts commas, so a trailing comma is simply a list where the two are equal.

Whitespace and comments are trivia. Up to and including the end of a line they belong to the token that ends that
line, and everything after belongs to the token that follows, so a comment on its own line attaches to the entry it
describes.

## Editing without reformatting

Nodes are immutable: every change returns a new node, and everything you did not change keeps its text.

```csharp
var root = TomlSyntaxTree.ParseText("port = 8080 # the port\n").GetRoot();
var port = (TomlIntegerSyntax)root.RootProperties[0].Value;

var updated = root.ReplaceNode(port, port.WithValue(9090));
Console.WriteLine(updated.ToFullString()); // port = 9090 # the port
```

`SyntaxFactory` builds new nodes. The methods that take .NET values write them the way TOML requires: keys are quoted
when they cannot be bare, strings are escaped, and floats always read back as floats.

```csharp
var document = SyntaxFactory.TomlDocument(
    SyntaxFactory.TomlTable("server"),
    SyntaxFactory.TomlProperty("host name", SyntaxFactory.TomlString("localhost")),
    SyntaxFactory.TomlProperty(SyntaxFactory.Key("limits", "max"), SyntaxFactory.TomlFloat(1)));

Console.WriteLine(document.ToFullString());
// [server]
// "host name" = "localhost"
// limits.max = 1.0
```

Every entry of a document has to end its line, so `TomlDocument` and `AddEntries` add a line feed to an entry that does
not already end with one. `SyntaxFactory.ParseValue` reads a single value, which is the easiest way to get one written
exactly as you want, such as a literal or multi-line string. A key defined twice in an inline table is not a grammar
mistake, so `ParseValue` does not report it; the tree the value ends up in does.

The factory methods refuse what TOML cannot hold rather than writing a document that does not read back: a string with
a lone surrogate, whitespace other than spaces and tabs, a line break other than `\n` and `\r\n`, a comment with a
control character, or `Token` for a kind whose text is not fixed, such as a key or a number.

## Diagnostics

Nothing is thrown for bad input; it is reported instead, with a location you can turn into line and character
positions.

```csharp
foreach (var diagnostic in TomlSyntaxTree.ParseText("a = 1\na = 2\n").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    Console.WriteLine($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
    // TOML0020 (1,0): The key 'a' is already defined.
}
```

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
| `TOML0012` | Arrays and inline tables nested deeper than `TomlParseOptions.MaxDepth` (128 by default) |
| `TOML0013` | A TOML 1.1 feature when parsing as TOML 1.0 |
| `TOML0020` | A key defined twice |
| `TOML0021` | A table defined twice |
| `TOML0022` | An addition to an inline table or an array, which cannot be extended after the fact |
| `TOML0023` | A table header or a dotted key that goes through a value |
| `TOML0024` | A dotted key that adds to a table a header already defined |

The first group is the grammar, and each diagnostic is carried by the node it is about: `ContainsDiagnostics` says
whether a node has any. The second group, from `TOML0020`, depends on the whole document rather than on one node, so
the tree works it out from its root when it is first asked. `TomlSyntaxTree.GetDiagnostics()` reports both, and so
does `GetDiagnostics()` on a node that is part of a tree. Because nothing about them is stored in the nodes, they are
always those of the tree at hand: a tree made from an edited root, with `WithRoot` or `Create`, is checked again, so a
duplicate removed by the edit is no longer reported and one the edit added is.

### Versions

`TomlParseOptions.Version` chooses the version of the specification. TOML 1.1, the default, adds line breaks and a
trailing comma in inline tables, the `\e` and `\xHH` escape sequences, and times written without seconds. Parsing as
TOML 1.0 reports each of them as `TOML0013` and otherwise reads them the same way.

```csharp
var tree = TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = TomlVersion.V1_0 });
```

### Recovering from mistakes

TOML is line-oriented, and so is recovery: a key/value pair or a table header that goes wrong never takes the next
line with it. A string without its closing quote ends with its line. Arrays, and inline tables in TOML 1.1, may span
lines, so they cannot stop at the end of one; they stop at their closing bracket, and also at a line that can only be
the start of the next entry, a table header or `key =`, where a comma or a closing bracket was expected. A missing `]`
is reported once, rather than turning the rest of the document into one array.

A table header is recognized by its whole line, `[key]` or `[[key]]` and nothing after it but a comment, so a line
such as `[3, 4]` in an array is an element whose comma is missing. In an inline table that opens a line of its own,
an indented `key =` is its next key/value pair whose comma is missing, where one at the start of a line ends it.

An array or an inline table nested deeper than `MaxDepth` is kept whole as skipped text, up to its own closing bracket,
and reported once; the arrays and inline tables around it still close where they should.

### What .NET cannot hold

Values are read into .NET types, which cannot hold a few valid TOML values. The year 0 and a leap second (`:60`) are
reported as `TOML0006`. An offset further from UTC than ±14:00 is read as the same instant in UTC. A fraction of a
second is truncated to the seven digits .NET keeps, as the specification asks. Line breaks in multi-line strings are
read as line feeds, whatever the text uses.

## Walking a tree

`TomlSyntaxWalker` visits every node, and with `SyntaxWalkerDepth.Token` or `SyntaxWalkerDepth.Trivia` every token
and trivium too. `TomlSyntaxRewriter` builds a new tree from the nodes you return; a node whose parts all come back
unchanged is returned as it was.

```csharp
private sealed class DoubleIntegers : TomlSyntaxRewriter
{
    public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node) => node.WithValue(node.Value * 2);
}
```

## Coming from version 1

Version 1 kept every value and key as raw text. Version 2 parses them:

- `TomlPropertySyntax.Key` is a `TomlKeySyntax` and `Value` a `TomlValueSyntax`, where they were strings.
  `KeyToken`, `SeparatorToken`, and `ValueNode` are replaced by `Key`, `EqualsToken`, and `Value`.
- `TomlTableSyntax.Key` replaces `NameToken` and `Name`. An array-of-tables header has the kind
  `SyntaxKind.TomlArrayOfTables`, and `[[` and `]]` are the tokens `OpenBracketOpenBracketToken` and
  `CloseBracketCloseBracketToken`.
- `SyntaxKind.KeyToken` and `SyntaxKind.ValueToken` are replaced by a kind per key and value token, and
  `TomlArraySyntax.Contents` by `Elements`.
- `SyntaxFactory.TomlProperty(string, string)` is `TomlProperty(string, TomlValueSyntax)`, and `Key(string)` and
  `Value(string)` are replaced by `Key(params string[])`, `KeyPart`, `Literal`, and `ParseValue`.
