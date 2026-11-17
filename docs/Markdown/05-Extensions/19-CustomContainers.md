---
title: Custom containers
description: Wrap blocks in a <div> of your class with :::name fences, and text in a <span> with ::.
---

[CustomContainers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.CustomContainers) groups blocks so that a
stylesheet or a script can treat them as one, such as a warning box, tabs or columns: blocks between a fence of three
colons or more, `:::name arguments`, and a fence as long are rendered as `<div class="name">`. Text between `::` is
rendered as `<span>`.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_CustomContainers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.CustomContainers };
var tree = MarkdownSyntaxTree.ParseText(":::warning Read this\nThis is ::important::.\n:::\n", options);

var container = Assert.IsType<MarkdownCustomContainerSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal("warning", container.Name);
Assert.Equal("Read this", container.Arguments);
Assert.Equal("<div class=\"warning\"><p>This is <span>important</span>.</p>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

A container is a [MarkdownCustomContainerSyntax](cref:Meziantou.Framework.Markdown.MarkdownCustomContainerSyntax), whose
[Name](cref:Meziantou.Framework.Markdown.MarkdownCustomContainerSyntax.Name) and
[Arguments](cref:Meziantou.Framework.Markdown.MarkdownCustomContainerSyntax.Arguments) give the text after the fence.
The closing fence closes the container and its blocks, even a code block. Text between `::` is a
[MarkdownEmphasisExtraSyntax](cref:Meziantou.Framework.Markdown.MarkdownEmphasisExtraSyntax) of style `CustomContainer`.

[Generic attributes](15-GenericAttributes.md) give a container other classes or attributes.

A [node renderer](../04-Rendering.md#write-some-nodes-yourself) writes the containers of a name as other HTML, such as
a `<details>` element.
