# Meziantou.Framework.Language.Ini

An immutable INI syntax tree that keeps every character of the text it was parsed from — comments, whitespace,
blank lines and all — and lets you edit it without reformatting anything you did not touch.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

Parsing never throws and never gives up. Whatever the text says, the tree reproduces it exactly, and anything wrong
with it is reported through `GetDiagnostics()`.

```csharp
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Ini;

var tree = IniSyntaxTree.ParseText("""
; comment
[database]
server=localhost
port: 5432
""");

Console.WriteLine(tree.GetDiagnostics().Count); // 0
Console.WriteLine(tree.GetRoot().ToFullString() == tree.GetText().Text); // True
```

## The shape of a tree

```
IniDocumentSyntax
├── Entries : SyntaxList<IniEntrySyntax>
│   ├── IniSectionSyntax   → OpenBracketToken, NameToken, CloseBracketToken
│   └── IniPropertySyntax  → KeyToken, SeparatorToken, ValueToken
└── EndOfFileToken
```

An entry is an `IniSectionSyntax`, an `IniPropertySyntax`, or `IniSkippedTextSyntax` for text the parser could not
use. Every entry is one line.

The entries are flat, as they are in the text: a section header is followed by the properties under it, not their
parent. `IniSectionSyntax.Properties` and `IniDocumentSyntax.GlobalProperties` group them, and `GetValue` looks one up:

```csharp
var root = IniSyntaxTree.ParseText("[database]\nport=5432\n").GetRoot();

Console.WriteLine(root.GetValue("database", "port")); // 5432
```

Section names and keys are compared case-insensitively unless you pass a comparer. When a key is there more than once,
`GetValue` returns the last one; `GetProperties` returns them all.

## Supported syntax

INI has no specification. The parser reads what the common dialects agree on:

- section headers such as `[section]`, `[my section]`, or `[remote "origin"]`
- key/value pairs with `=` or `:`; a key runs up to the separator, so `my key` and `Name[fr]` are keys
- comments on their own line, starting with `;` or `#`
- values in double or single quotes, such as `"a;b"`, where `;` and `#` are not comments; `Value` leaves the quotes
  out, and nothing inside them is escaped
- whitespace around keys, section names, and values, which is trivia and not part of them

Where the dialects differ, `IniParseOptions` chooses:

| Option | Default | What it changes |
|---|---|---|
| `InlineComments` | `AfterWhitespace` | Whether `;` and `#` start a comment after a key or value: never (`None`), when whitespace precedes them (`AfterWhitespace`, so `url=http://host/#top ; note` has the value `http://host/#top`), or anywhere (`Anywhere`, so `password=abc#123` has the value `abc`). |
| `AllowKeysWithoutValue` | `false` | Whether a line holding only a key, such as `skip-networking`, is a valid property rather than an error. |
| `AllowMultilineValues` | `false` | Whether lines indented more than the key continue its value, as Python's `configparser` reads them. |
| `ReportDuplicates` | `false` | Whether a section name used twice, or a key used twice in a section, is reported as a warning. |
| `NameComparer` | `OrdinalIgnoreCase` | How names are compared to find duplicates. |

```csharp
var tree = IniSyntaxTree.ParseText(text, new IniParseOptions { AllowKeysWithoutValue = true });
```

## Editing without reformatting

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the document is untouched — and the result is the same type you started with, so no cast is needed.

```csharp
var tree = IniSyntaxTree.ParseText("name=old");
var property = (IniPropertySyntax)tree.GetRoot().Entries[0];

IniDocumentSyntax updated = tree.GetRoot().ReplaceNode(property, property.WithValue("new"));

Console.WriteLine(updated.ToFullString()); // name=new
```

`WithValue` writes the value so that it reads back the same, whatever the comment mode: it quotes a value that holds
`;` or `#`, has whitespace around it, or is itself in quotes. The factory methods that take a key, a section name, or a
value throw when the text would read back as something else, so an edit cannot turn into a new section or cut a value
short. `SyntaxFactory.IniProperty(key, value)` and `SyntaxFactory.IniSection(name)` end with a line feed, and
`AddEntries` ends the line of the last entry before adding to it, so new entries never run into the lines around them.
