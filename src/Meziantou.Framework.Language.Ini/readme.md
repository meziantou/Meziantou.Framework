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
│   └── IniPropertySyntax  → KeyToken, SeparatorToken, ValueToken, ContinuationTokens
└── EndOfFileToken
```

An entry is an `IniSectionSyntax`, an `IniPropertySyntax`, or `IniSkippedTextSyntax` for text the parser could not
use. Every entry is one line, except a property whose value continues on the lines below it.

The entries are flat, as they are in the text: a section header is followed by the properties under it, not their
parent. `IniSectionSyntax.Properties` and `IniDocumentSyntax.GlobalProperties` group them, and `GetValue` looks one up:

```csharp
var root = IniSyntaxTree.ParseText("[database]\nport=5432\n").GetRoot();

Console.WriteLine(root.GetValue("database", "port")); // 5432
```

Section names and keys are compared with the `NameComparer` the document was parsed with, case-insensitively by default,
unless you pass a comparer. When a key is there more than once, `GetValue` returns the last one; `GetProperties` returns
them all.

## Supported syntax

INI has no specification. The parser reads what the common dialects agree on:

- section headers such as `[section]`, `[my section]`, or `[remote "origin"]`
- key/value pairs with `=` or `:`; a key runs up to the separator, so `my key` and `Name[fr]` are keys
- comments on their own line, starting with `;` or `#`
- values wholly in double or single quotes, such as `"a;b"`, where `;` and `#` are not comments; `Value` leaves the
  quotes out, and nothing inside them is escaped (unless `AllowQuotedValues` is off). When anything but a comment
  follows the closing quote, as in `"a ;b" c`, the quotes are ordinary characters and the line is read as if there were
  none, so its value is `"a`
- whitespace around keys, section names, and values, which is trivia and not part of them

Where the dialects differ, `IniParseOptions` chooses:

| Option | Default | What it changes |
|---|---|---|
| `InlineComments` | `AfterWhitespace` | Whether `;` and `#` start a comment after a key or value: never (`None`), when whitespace precedes them (`AfterWhitespace`, so `url=http://host/#top ; note` has the value `http://host/#top`), or anywhere (`Anywhere`, so `password=abc#123` has the value `abc`). |
| `AllowKeysWithoutValue` | `false` | Whether a line holding only a key, such as `skip-networking`, is a valid property rather than an error. |
| `AllowMultilineValues` | `false` | Whether lines indented more than the key continue its value, as Python's `configparser` reads them: comment lines between them are skipped, and blank lines between them are empty lines of the value. Each line is a token of `ContinuationTokens`, so comments stay trivia. A line indented under a key without a value is reported (`INI0008`), as `configparser` rejects it. |
| `AllowQuotedValues` | `true` | Whether a value wholly in quotes is read without them. Turn it off for dialects where quotes are ordinary characters, such as Python's `configparser`. |
| `ReportDuplicates` | `false` | Whether a section name used twice, or a key used twice in a section, is reported as a warning. |
| `NameComparer` | `OrdinalIgnoreCase` | How names are compared to find duplicates, and by `GetValue` and the other lookups, which find a name without reading every entry when they use it. |

The options travel with the document: `IniDocumentSyntax.Options` is what the tree was parsed with, and every document an
edit makes from it keeps them, as does `IniSyntaxTree.WithRoot`. `IniSyntaxTree.Create(root, options)` with other options
parses the text of the root again, so the tree reads its text as those options say.

For Python's `configparser`, whose defaults are whole-line comments only, unquoted values, and multiline values:

```csharp
var python = new IniParseOptions
{
    InlineComments = IniInlineCommentMode.None,
    AllowQuotedValues = false,
    AllowMultilineValues = true,
};
```

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

`WithValue` writes the value so that the document reads it back the same with its own options, and quotes it only when
they require it: when a `;` or `#` would start a comment, when it has whitespace around it, or when it starts with a
quote, which a quote in the comment after it could otherwise close. When `AllowQuotedValues` is off, it never adds
quotes and throws instead, and it throws too when the comment it keeps would read as part of the value. A value with
line breaks continues on indented lines, which needs `AllowMultilineValues`; its lines read back joined with `\n`,
whichever of `\r\n`, `\r`, or `\n` separated them. A property that is not part of a document quotes the value so that
it reads back whatever the comment mode, as `SyntaxFactory.Value(value)` does; `SyntaxFactory.Value(value, options)` and
`SyntaxFactory.IniProperty(key, value, options)` write for given options.

The factory methods that take a key, a section name, or a value throw when the text would read back as something else,
so an edit cannot turn into a new section or cut a value short. Without options, they check it whatever the comment
mode; `SyntaxFactory.Key(text, options)` and `SyntaxFactory.SectionName(text, options)` check it for given options, so
that `C#` is a key when `#` only starts a comment after whitespace. `WithKey`, `WithName`, and `SetValue` check names
with the options of the document. `SyntaxFacts.IsValidKey`, `IsValidSectionName`, and `IsValidValue` tell without
throwing.

However a document is edited — `ReplaceNode`, `InsertNodesAfter`, a rewriter — its entries stay on lines of their own
and read back as they are in the tree:

- an entry that another one, or a comment at the end of the document, follows is given a line break if it has none, and
  so is each line of a value that continues on the lines below it;
- skipped text stays on the line of the section header it follows, and goes with it when the header is removed;
- with `AllowMultilineValues`, an entry after a property is indented no more than its key, so that it does not continue
  its value, and the lines a value continues on are indented more than its key.

`AddEntries` writes the new entries with the line breaks of the document, after the header comment of a document that
has nothing else.

For the common edits, the document does the work, writing new properties like the ones around them:

```csharp
var root = IniSyntaxTree.ParseText("[database]\n  port = 5432\n").GetRoot();

root = root.SetValue("database", "host", "localhost"); // adds "  host = localhost" under port
root = root.RemoveProperties("database", "port");      // removes every "port" in the section, and its comments
root = root.RemoveSections("legacy");                  // removes the headers and everything under them
```

A new section goes at the end of the document, after the comments that end it. The comment lines right above a removed
property or section go with it; comment lines that a blank line separates from it head a group of entries, and stay.

Every edit of an immutable document rebuilds it, so setting many properties one at a time costs as much as the document
for each of them. `SetValues` sets every property the document already has in one edit:

```csharp
root = root.SetValues([("database", "host", "db1"), ("database", "port", "5433")]);
```

## Limitations

- Nothing is escaped: there is no `\"`, `\n`, or `\;`, so a value that needs quotes and holds both kinds of quote, or a
  value with a line break outside `AllowMultilineValues`, cannot be written.
- A line ending with `\` does not continue on the next one, as it does in git config files.
- Git subsections such as `[remote "origin"]` are read as a section named `remote "origin"`, and a `]` inside the quotes
  ends the header.
- Keys are separated from values by `=` or `:` only, so a key cannot hold either.
- A property whose value continues on several lines reads back as one only in a document read with
  `AllowMultilineValues`; putting one in another document writes lines that it reads as entries of their own.
- `WithChanges` parses the whole text again.
