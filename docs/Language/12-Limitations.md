---
title: Limitations
description: What the language packages do not do, and what to use instead.
---

## Parsers, not processors

The packages read the syntax of a document. They do not apply what it means:

- **XML**: the parser checks that a document is well-formed, not that it is valid. It does not apply a DTD or a schema,
  and does not resolve entities other than the predefined ones and character references.
- **CSS**: there is no grammar for the value of each property and no cascade. SCSS and Less are not supported.
- **Regular expressions**: nothing matches text; use `System.Text.RegularExpressions`. Java, Python and Go patterns are
  not supported, and the `Net` dialect follows the current .NET release. See [Dialects](09-Regex/02-Dialects.md).
- **Shell scripts**: nothing runs, and nothing is expanded: the value of a word that depends on a variable or a command
  substitution is `null`.
- **INI**: there is no escape sequence, no line continuation with `\`, and git subsections are plain section names. See
  [INI](06-Ini.md#limitations).

## Memory

A tree has a node or a token for each part of the text, with its trivia, so it takes much more memory than the data it
describes. Use it for the documents you edit or analyze. To load data into your own types, use a serializer, such as
`System.Text.Json` for JSON or [`Meziantou.Framework.Toml`](../Toml/01-Introduction.md) for TOML.

The trees of the versions of a document share the nodes an edit did not touch, so keeping several versions costs little
more than keeping one.

## Your own language

The packages share their tree with the languages of this page only: the types that store the text of a tree are
internal, so you cannot build a parser for another language on `Meziantou.Framework.Language`.

## Formatting

Only XML has a formatter. The other languages never reformat what is there: a node built by code has the whitespace its
factory writes, which may be none, as in JSON, and you give it the trivia it needs. See
[Build nodes](03-Concepts/06-EditingTrees.md#build-nodes).
