---
title: Overview
description: The model every language shares, from the tree to the visitors.
---

Every language package builds the same kind of tree, with the types of `Meziantou.Framework.Language`. What you learn
with one language applies to the others: only the node types and the `SyntaxKind` values differ.

A `SyntaxTree` holds the text it was parsed from and a root node. Nodes hold other nodes and tokens, the words and the
punctuation of the text; tokens hold trivia, the whitespace and comments around them. Every character of the text
belongs to exactly one token or trivia, so the tree writes the text back exactly.

| Page | Covers |
| --- | --- |
| [Nodes and tokens](02-NodesAndTokens.md) | The parts of a tree, kinds, lists, invalid text in the tree, and finding a node |
| [Trivia](03-Trivia.md) | Whitespace and comments, and the difference between `Span` and `FullSpan` |
| [Source text and positions](04-SourceTextAndPositions.md) | `SourceText`, `TextSpan`, lines, and `Location` |
| [Diagnostics](05-Diagnostics.md) | The problems a parser reports, their severity, and their position |
| [Editing trees](06-EditingTrees.md) | Replacing, inserting and removing nodes, building them, and editing by text |
| [Annotations](07-Annotations.md) | Marking a node to find it again after an edit |
| [Visitors and rewriters](08-VisitorsAndRewriters.md) | Walking a tree, and building a new one from it |

## Immutable and shared

A tree never changes. An edit returns a new tree that shares every node it did not touch with the old one, so:

- you can keep several versions of a document for little more memory than one;
- you can read a tree from several threads;
- a node you hold stays valid after an edit, but belongs to the old tree: find its counterpart in the new tree with an
  [annotation](07-Annotations.md).

## Parsing never throws

Whatever the text says, the parser returns a tree that reproduces it, and reports what is wrong as
[diagnostics](05-Diagnostics.md). It keeps the shape the author most likely meant, so one mistake gives one diagnostic
rather than a cascade, and the rest of the document is read as usual.
