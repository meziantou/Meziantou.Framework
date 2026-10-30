---
title: Introduction
description: Syntax trees that keep every character of CSS, INI, JSON, TOML, XML, regular expressions and shell scripts, for tools that read, check and edit them.
---

The `Meziantou.Framework.Language.*` packages parse text into an immutable syntax tree that keeps every character of the
input: whitespace, comments, quotes, line breaks, and even the mistakes. Write the tree back and you get the input
exactly; change one node and only that part of the text changes. They are made for tools that work on the text itself,
such as a formatter, a linter, an editor feature, or a script that updates a configuration file without rewriting it.

## What you can do with them

- **Read a document as it is written**: walk its nodes, find the node at a position, read the comments. See
  [Nodes and tokens](03-Concepts/02-NodesAndTokens.md) and [Trivia](03-Concepts/03-Trivia.md).
- **Report problems with their position**: parsing never throws, whatever the input, and every problem is a diagnostic
  with a line and a column. See [Diagnostics](03-Concepts/05-Diagnostics.md).
- **Edit without reformatting**: replace, insert or remove a node, and keep the formatting and the comments of
  everything else. See [Editing trees](03-Concepts/06-EditingTrees.md).
- **Inspect a tree from the command line**: dump it as JSON without writing any C#. See
  [Command-line tool](11-CommandLineTool.md).

## Packages

Every language is a package of its own. Each one references `Meziantou.Framework.Language`, the package that holds what
they share, so a span, a text change, or a diagnostic means the same thing whichever language produced it.

| Package | Parses | Entry point |
| --- | --- | --- |
| `Meziantou.Framework.Language.Css` | Style sheets and declaration lists, with nesting, selectors and at-rules | `CssSyntaxTree`. See [CSS](08-Css.md). |
| `Meziantou.Framework.Language.Ini` | INI files, in the dialect you choose | `IniSyntaxTree`. See [INI](06-Ini.md). |
| `Meziantou.Framework.Language.Json` | JSON, with comments and trailing commas | `JsonSyntaxTree`. See [JSON](04-Json.md). |
| `Meziantou.Framework.Language.Regex` | Regular expression patterns: .NET, JavaScript, PCRE, POSIX | `RegexSyntaxTree`. See [Regular expressions](09-Regex/01-Overview.md). |
| `Meziantou.Framework.Language.Shell` | Scripts: sh, bash, zsh, PowerShell, cmd | `ShellSyntaxTree`. See [Shell scripts](10-Shell/01-Overview.md). |
| `Meziantou.Framework.Language.Toml` | TOML 1.0 and 1.1 | `TomlSyntaxTree`. See [TOML](07-Toml.md). |
| `Meziantou.Framework.Language.Xml` | XML, with XPath queries and a formatter | `XmlSyntaxTree`. See [XML](05-Xml.md). |
| `Meziantou.Framework.Language.Tool` | All of the above, from the command line | `Meziantou.Framework.Language.Tool syntax`. See [Command-line tool](11-CommandLineTool.md). |

`Meziantou.Framework.Language` is not useful on its own: install the package of the language you parse, and it comes
with it.

## Choose the right tool

A syntax tree is the right model when the text matters: its layout, its comments, the position of each part. When you
only need the data, a serializer is simpler and uses less memory.

| You want to | Use |
| --- | --- |
| Update a value in a `package.json`, a `.csproj`, an `.editorconfig` or a `Cargo.toml`, and keep the rest of the file as it is | The language package: [Editing trees](03-Concepts/06-EditingTrees.md) |
| Report the problems of a file with their line and column | The language package: [Diagnostics](03-Concepts/05-Diagnostics.md) |
| Find which group of a regular expression, or which command of a script, is at a position | `RegexSyntaxTree` or `ShellSyntaxTree` with `FindToken` |
| Load a TOML file into your own types, or save them | `Meziantou.Framework.Toml`: see [TOML](../Toml/01-Introduction.md) |
| Highlight code in a web page | `Meziantou.Framework.SyntaxHighlighting`: see [Syntax highlighting](../SyntaxHighlighting/01-Introduction.md) |

`Meziantou.Framework.Language.Toml` and `Meziantou.Framework.Toml` both parse TOML, for different purposes: the first
gives you a tree to read and edit the text, with the same model as the other languages here; the second maps documents to
.NET objects. See [Two TOML packages](07-Toml.md#two-toml-packages).

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Language](cref:Meziantou.Framework.Language) | What every language shares: `SyntaxTree`, `SyntaxNode`, `SyntaxToken`, `SyntaxTrivia`, the lists, `SourceText`, `TextSpan`, `TextChange`, `Location`, `Diagnostic`, `SyntaxAnnotation`, and the editing extension methods (`ReplaceNode`, `RemoveNode`, …) |
| `Meziantou.Framework.Language.<Language>` | The tree, the nodes, the options, the visitors, `SyntaxKind`, `SyntaxFactory` and `SyntaxFacts` of one language, such as `Meziantou.Framework.Language.Json` |

Each language namespace is a child of [Meziantou.Framework.Language](cref:Meziantou.Framework.Language), so a file that has
`using Meziantou.Framework.Language.Json;` can use the shared types without a second `using` directive. The examples of
this documentation omit the `using` directives.

## Next steps

- [Getting started](02-GettingStarted.md): install a package, parse a file, report its problems and edit it.
- [Concepts](03-Concepts/01-Overview.md): the model every language shares.
- The page of your language, from [JSON](04-Json.md) to [Shell scripts](10-Shell/01-Overview.md).
