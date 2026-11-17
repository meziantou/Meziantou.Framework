---
title: Introduction
description: A CommonMark and GitHub Flavored Markdown parser for .NET, whose syntax tree keeps every character of the text, with an HTML renderer, extensions and a formatter.
---

`Meziantou.Framework.Markdown` parses Markdown and converts it to HTML in .NET. It follows
[CommonMark](https://commonmark.org/) 0.31.2 and [GitHub Flavored Markdown](https://github.github.com/gfm/), and its
extensions add other syntaxes, such as alerts, footnotes, definition lists
and grid tables.

The syntax tree keeps every character of the text it was parsed from, whitespace and markers included: a document is
written back exactly, and an edit only changes the text of what it replaces. The tree is built on
[Meziantou.Framework.Language](../Language/01-Introduction.md), as the CSS, JSON or XML trees are.

## What you can do with it

- **Convert Markdown to HTML or to plain text** with one call. See [Getting started](02-GettingStarted.md).
- **Inspect and edit a document**: find the headings or the links, replace a node, insert blocks, and write the text
  back without changing the rest of it. See [The syntax tree](03-SyntaxTree.md).
- **Control the HTML**: make the URLs absolute, rewrite them, add `rel` to the links, or map the lines of the source to
  the elements. See [Rendering](04-Rendering.md).
- **Enable extensions**: tables, task lists, alerts, footnotes, heading identifiers, emojis, mathematics, diagrams, YAML
  front matter and more. See [Extensions](05-Extensions/01-Overview.md).
- **Format a document** in a consistent style, without changing its HTML. See [Formatting](06-Formatting.md).
- **Render the Markdown of your users** safely. See [Security](07-Security.md).

Version 2.0 replaces the parser of version 1.x, a fork of Markdig. See [Migrating from 1.x](08-MigratingFrom1x.md).
