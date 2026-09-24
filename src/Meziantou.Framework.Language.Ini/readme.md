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
use.

## Supported syntax

The parser targets common INI syntax:

- section headers such as `[section]`
- key/value pairs with `=` or `:`
- comments starting with `;` or `#`
- blank lines and whitespace as trivia
- raw value text

## Editing without reformatting

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the document is untouched — and the result is the same type you started with, so no cast is needed.

```csharp
var tree = IniSyntaxTree.ParseText("name=old");
var property = (IniPropertySyntax)tree.GetRoot().Entries[0];

IniDocumentSyntax updated = tree.GetRoot().ReplaceNode(property, property.WithValue("new"));

Console.WriteLine(updated.ToFullString()); // name=new
```
