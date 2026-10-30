---
title: Syntax tree, lexer and parser
description: Parse TOML into a lossless syntax tree for formatters and linters, or read tokens and parse events without building a model.
---

The serializer and the document model give you the values of a document. Tools that work on the text itself, such as a
formatter, a linter or an editor feature, need more: where each value is, its comments, and its exact spelling. The
types of this page are in the [Meziantou.Framework.Toml.Parsing](cref:Meziantou.Framework.Toml.Parsing) namespace, and the nodes of the tree in
[Meziantou.Framework.Toml.Syntax](cref:Meziantou.Framework.Toml.Syntax).

| You want to | Use |
| --- | --- |
| Read every character of a document, including comments and whitespace | `SyntaxParser` |
| Read the tokens of a document | `TomlLexer` |
| Read the structure of a document without building a tree or a model | `TomlParser` |

## Parse a syntax tree

`SyntaxParser.Parse` builds a tree that keeps every character of the input: `ToString()` returns the original text.
Walk it with `Descendants()`:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=SyntaxParser_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var input = "# config\nname = \"Ada\"\n";
var document = SyntaxParser.Parse(input, sourceName: "config.toml");
Assert.False(document.HasErrors);

Assert.Equal(input, document.ToString()); // Same text as the input

var values = new List<string?>();
foreach (var node in document.Descendants())
{
    if (node is StringValueSyntax value)
        values.Add(value.Value);
}

Assert.Equal(["Ada"], values);
```
<!-- endref -->

- A `DocumentSyntax` holds the root `KeyValues` and the `Tables`. Every node has `LeadingTrivia` and `TrailingTrivia`,
  the comments and whitespace around it.
- Derive from `SyntaxVisitor` to walk the tree, and use `Tokens()` to enumerate its tokens.
- `DocumentSyntax.HasByteOrderMark` says whether the input started with a byte order mark.

### Errors

`SyntaxParser.Parse` does not throw on invalid TOML: it collects the errors in `Diagnostics`, and `HasErrors` says whether
there are any. `SyntaxParser.ParseStrict` throws a `TomlException` instead. The tree of an invalid document still keeps
every character: the tokens the parser skips are kept as trivia, and an `InvalidSyntaxToken` holds the text found in
place of an expected token.

### Memory

Each token and each trivia of the tree is an object with its own position, so a tree uses a lot of memory compared with
the size of its input. `Diagnostics` keeps every error, so a document with many errors uses more. To read large or
untrusted input, use `TomlSerializer` or `TomlParser` instead.

## Read tokens and parse events

`TomlLexer` returns the tokens of a document, and `TomlParser` returns parse events, such as the start of a table or a
key, without building a tree. Both avoid allocations where they can:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlLexer_TomlParser;format=md-fenced-code-block;dedent=true -->
```csharp
var tokens = new List<string>();
var lexer = TomlLexer.Create("name = \"Ada\"", sourceName: "config.toml");
while (lexer.MoveNext())
{
    tokens.Add($"{lexer.Current.Kind} {lexer.CurrentSpan}");
}

Assert.Equal(
[
    "BasicKey config.toml(1,1)-(1,4)",
    "Whitespaces config.toml(1,5)-(1,5)",
    "Equal config.toml(1,6)-(1,6)",
    "Whitespaces config.toml(1,7)-(1,7)",
    "String config.toml(1,8)-(1,12)",
    "Eof config.toml(1,13)-(1,13)",
], tokens);

var events = new List<string>();
var parser = TomlParser.Create("[server]\nport = 8080", new TomlParserOptions { Mode = TomlParserMode.Tolerant });
while (parser.MoveNext())
{
    var kind = parser.Current.Kind;
    events.Add(kind == TomlParseEventKind.PropertyName ? $"{kind} {parser.GetPropertyName()}" : kind.ToString());
}

Assert.Equal(
[
    "StartDocument",
    "StartTable",
    "PropertyName server",
    "StartTable",
    "PropertyName port",
    "Integer",
    "EndTable",
    "EndTable",
    "EndDocument",
], events);
```
<!-- endref -->

`TomlParser` returns the events in the order of the document: a table defined in several places, for example with
dotted keys, produces several `PropertyName` and `StartTable` events with the same name.

`TomlParserOptions` sets:

- the error mode: `Strict` throws on the first error, `Tolerant` collects the errors;
- whether escape sequences are decoded;
- whether trivia is reported;
- whether string values are created eagerly.
