---
title: INI
description: Parse INI files in the dialect you choose, read values by section and key, set and remove them without reformatting the file.
---

`Meziantou.Framework.Language.Ini` parses INI files, such as `.editorconfig`, `.gitconfig` or a `setup.cfg`, into a tree
that keeps every character. It reads values by section and key, and sets them by writing new lines like the ones around
them.

```bash
dotnet add package Meziantou.Framework.Language.Ini
```

## Parse a file

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = IniSyntaxTree.ParseText("""
; comment
[database]
server=localhost
port: 5432
""");

Assert.Empty(tree.GetDiagnostics());
Assert.Equal(tree.GetText().Text, tree.GetRoot().ToFullString());
```
<!-- endref -->

An `IniDocumentSyntax` holds its `Entries` in the order of the text. An entry is an `IniSectionSyntax` (`[name]`), an
`IniPropertySyntax` (`key=value`), or an `IniSkippedTextSyntax` for a line the parser could not use. Each entry is one
line, except a property whose value continues on the lines below it.

The entries are flat, as in the text: a section header is followed by its properties rather than being their parent.
`IniSectionSyntax.Properties` and `IniDocumentSyntax.GlobalProperties`, the properties before the first section, group
them.

## Read values

`GetValue` finds the value of a key, in a section or, with a `null` section, before the first one:

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_GetValue;format=md-fenced-code-block;dedent=true -->
```csharp
var root = IniSyntaxTree.ParseText("[database]\nport=5432\n").GetRoot();

Assert.Equal("5432", root.GetValue("database", "port"));
```
<!-- endref -->

- Names are compared with the `NameComparer` of the options, case-insensitively by default, unless you pass a comparer.
- When a key is there more than once, `GetValue` returns the last one, and `GetProperties` returns them all.
  `GetSections` returns every section of a name.
- `IniPropertySyntax.Value` is the value without its quotes; `Key` and `IniSectionSyntax.Name` are the names without the
  whitespace around them.

## What is read

INI has no specification. The parser reads what the common dialects agree on:

- section headers such as `[section]`, `[my section]`, or `[remote "origin"]`;
- `key=value` and `key: value`; a key runs up to the separator, so `my key` and `Name[fr]` are keys;
- comments on their own line, starting with `;` or `#`;
- values wholly in double or single quotes, such as `"a;b"`, where `;` and `#` do not start a comment; nothing inside
  the quotes is escaped. When something other than a comment follows the closing quote, as in `"a ;b" c`, the quotes are
  ordinary characters;
- whitespace around keys, section names and values, which is trivia.

## Choose a dialect

Where the dialects differ, `IniParseOptions` decides:

| Option | Default | Changes |
| --- | --- | --- |
| `InlineComments` | `AfterWhitespace` | Whether `;` and `#` start a comment after a key or a value: never (`None`), when whitespace precedes them (`AfterWhitespace`: `url=http://host/#top ; note` has the value `http://host/#top`), or anywhere (`Anywhere`: `password=abc#123` has the value `abc`) |
| `AllowKeysWithoutValue` | `false` | Whether a line with only a key, such as `skip-networking`, is a property rather than an error |
| `AllowMultilineValues` | `false` | Whether the lines indented more than a key continue its value. Comment lines between them are skipped, and blank lines are empty lines of the value |
| `AllowQuotedValues` | `true` | Whether a value wholly in quotes is read without them |
| `ReportDuplicates` | `false` | Whether a section or a key defined twice is reported as a warning |
| `NameComparer` | `OrdinalIgnoreCase` | How names are compared, by the lookups and to find duplicates |

For example, the dialect of Python's `configparser` has whole-line comments only, no quoted values, and multi-line
values:

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_PythonOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var python = new IniParseOptions
{
    InlineComments = IniInlineCommentMode.None,
    AllowQuotedValues = false,
    AllowMultilineValues = true,
};

var root = IniSyntaxTree.ParseText("[paths]\nhome = \"/home/user\" ; not a comment\nlist = a\n  b\n", python).GetRoot();
Assert.Equal("\"/home/user\" ; not a comment", root.GetValue("paths", "home"));
Assert.Equal("a\nb", root.GetValue("paths", "list"));
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_KeysWithoutValue;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "[mysqld]\nskip-networking\n";
var tree = IniSyntaxTree.ParseText(text, new IniParseOptions { AllowKeysWithoutValue = true });

Assert.Empty(tree.GetDiagnostics());
```
<!-- endref -->

The options travel with the document: `IniDocumentSyntax.Options` is what it was parsed with, and every document an edit
makes from it keeps them. `IniSyntaxTree.Create(root, options)` with other options parses the text of the root again,
so it is read as those options say.

## Edit values

The document has methods for the common edits. They write new properties like the ones around them, with the same
indentation and spacing:

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_CommonEdits;format=md-fenced-code-block;dedent=true -->
```csharp
var root = IniSyntaxTree.ParseText("[database]\n  port = 5432\n[legacy]\nold = 1\n").GetRoot();

root = root.SetValue("database", "host", "localhost"); // adds "  host = localhost" under port
Assert.Equal("[database]\n  port = 5432\n  host = localhost\n[legacy]\nold = 1\n", root.ToFullString());

root = root.RemoveProperties("database", "port");      // removes every "port" in the section, and its comments
root = root.RemoveSections("legacy");                  // removes the headers and everything under them
Assert.Equal("[database]\n  host = localhost\n", root.ToFullString());
```
<!-- endref -->

- `SetValue` changes the value of a key, or adds the property at the end of its section. A new section goes at the end
  of the document, after the comments that end it.
- `RemoveProperties` and `RemoveSections` remove every match. The comment lines right above a removed entry go with it;
  comment lines that a blank line separates from it stay.
- Every edit rebuilds the document, so setting many properties one at a time costs as much as the document for each.
  `SetValues` sets the properties the document already has in one edit:

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_SetValues;format=md-fenced-code-block;dedent=true -->
```csharp
var root = IniSyntaxTree.ParseText("[database]\nhost = localhost\nport = 5432\n").GetRoot();

root = root.SetValues([("database", "host", "db1"), ("database", "port", "5433")]);
Assert.Equal("[database]\nhost = db1\nport = 5433\n", root.ToFullString());
```
<!-- endref -->

To edit a single property, replace it with `WithValue`, `WithKey` or `WithName`:

<!-- ref:../../tests/Meziantou.Framework.Language.Ini.Tests/IniSyntaxTreeTests.cs;region=IniSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = IniSyntaxTree.ParseText("name=old");
var property = (IniPropertySyntax)tree.GetRoot().Entries[0];

IniDocumentSyntax updated = tree.GetRoot().ReplaceNode(property, property.WithValue("new"));

Assert.Equal("name=new", updated.ToFullString());
```
<!-- endref -->

### Values that read back

`WithValue` and `SetValue` write a value so that the document reads it back as the same value with its own options:

- They quote the value only when the options require it: when a `;` or `#` would start a comment, when it has
  whitespace around it, or when it starts with a quote.
- When `AllowQuotedValues` is off, they never add quotes, and throw an `ArgumentException` instead.
- A value with line breaks continues on indented lines, which needs `AllowMultilineValues`.

The `SyntaxFactory` methods that take a key, a section name, or a value throw when the text would read back as something
else, so an edit cannot create a new section or cut a value short. `SyntaxFacts.IsValidKey`, `IsValidSectionName` and
`IsValidValue` tell without throwing.

However you edit a document, with these methods, `ReplaceNode`, `InsertNodesAfter` or a rewriter, its entries stay on
lines of their own: an entry that another one follows gets a line break if it has none.

## Diagnostics

| Id | Reported for |
| --- | --- |
| `INI0001` | A missing section name |
| `INI0002` | A missing `]` |
| `INI0003` | A missing key |
| `INI0004` | A missing `=` or `:` |
| `INI0005` | Text after a section header or a property, where only a comment may follow |
| `INI0006` | A section defined twice, with `ReportDuplicates` (a warning) |
| `INI0007` | A key defined twice in a section, with `ReportDuplicates` (a warning) |
| `INI0008` | A line indented under a key that has no value, with `AllowMultilineValues` |

## Limitations

- Nothing is escaped: there is no `\"`, `\n` or `\;`. A value that needs quotes and holds both kinds of quote, or a
  value with a line break without `AllowMultilineValues`, cannot be written.
- A line that ends with `\` does not continue on the next one, as it does in git configuration files.
- Git subsections such as `[remote "origin"]` are a section named `remote "origin"`, and a `]` inside the quotes ends
  the header.
- Keys are separated from values by `=` or `:` only, so a key cannot hold either.
- `WithChanges` parses the whole text again.
