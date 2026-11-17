---
title: Task lists
description: Write checklists with list items that start with [ ] or [x], as GitHub does.
---

[TaskLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.TaskLists) turns list items into checklist items, to
track what is done in an issue, a pull request or a plan: an item that starts with `[ ]` is unchecked, and one that
starts with `[x]` or `[X]` is checked. The renderer writes a disabled checkbox before the text of the item.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_TaskLists;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.TaskLists };
var tree = MarkdownSyntaxTree.ParseText("- [x] Write the draft\n- [ ] Review it\n", options);

var items = tree.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>().ToList();
Assert.True(items[0].IsTask);
Assert.True(items[0].IsChecked);
Assert.False(items[1].IsChecked);
Assert.Equal("<ul>\n<li><input checked=\"\" disabled=\"\" type=\"checkbox\"> Write the draft</li>\n<li><input disabled=\"\" type=\"checkbox\"> Review it</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

[MarkdownListItemSyntax.IsTask](cref:Meziantou.Framework.Markdown.MarkdownListItemSyntax.IsTask) tells whether an item
is a task, and [IsChecked](cref:Meziantou.Framework.Markdown.MarkdownListItemSyntax.IsChecked) whether it is checked.
The `[ ]` or `[x]` is a token of the list item, after its marker:
[TaskListItemMarkerToken](cref:Meziantou.Framework.Markdown.MarkdownListItemSyntax.TaskListItemMarkerToken).

Unlike cmark-gfm, a task list item marker is recognized in any container, such as a block quote, and needs text after
it.
