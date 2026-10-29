---
title: Introduction
description: Convert the rich text of Jira, Confluence and Bitbucket to Markdown, and read or build Atlassian Document Format documents.
---

Jira, Confluence and Bitbucket store rich text, such as the description of a Jira issue or a comment, in the
Atlassian Document Format (ADF): a JSON tree of nodes (paragraphs, tables, panels, mentions…) and marks (bold, links,
colors…). `Meziantou.Framework.AtlassianDataFormat` reads these documents and converts them to Markdown.

## What you can do with it

- **Convert a document to Markdown**, to show an issue in a terminal, index it, or pass it to a tool that reads
  Markdown. The options choose how constructs Markdown cannot express, such as panels and collapsible sections, are
  written. See [Converting to Markdown](03-ConvertingToMarkdown.md).
- **Read a document** through a typed object model: find the mentions, links or attachments of an issue. See
  [The object model](04-ObjectModel.md).
- **Build a document** in code, and write it as JSON to send it to an Atlassian API.
- **Convert from the command line** with the `Meziantou.Framework.AtlassianDataFormat.Tool` .NET tool. See
  [Command-line tool](05-CommandLineTool.md).

Parsing never fails on a node or mark type the library does not know: Atlassian adds new ones regularly, and they are
kept as they are.

## Namespace

Every type is in the `Meziantou.Framework.AtlassianDataFormat` namespace: `AdfToMarkdown`, `AdfToMarkdownOptions`,
`AdfDocument`, `AdfNode` and its derived types, and `AdfMark` and its derived types.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and convert a document.
- [Limitations](06-Limitations.md): what the library does not do.
