---
title: Alerts
description: Highlight a note, a tip or a warning with a block quote that starts with [!NOTE], as GitHub does.
---

[Alerts](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Alerts) adds the alerts of GitHub, which make important
information stand out from the text: a block quote whose first line is `[!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]`
or `[!CAUTION]` is rendered as a `<div class="markdown-alert markdown-alert-warning">`, with a title and the icon of its
kind, as GitHub renders it.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Alerts;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Alerts };
var tree = MarkdownSyntaxTree.ParseText("> [!WARNING]\n> Back up your files first.\n", options);

var alert = Assert.IsType<MarkdownAlertSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal("WARNING", alert.AlertKind);

var html = MarkdownHtmlRenderer.ToHtml(tree);
Assert.StartsWith("<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"markdown-alert-title\"><svg", html);
Assert.EndsWith("</svg>Warning</p>\n<p>Back up your files first.</p>\n</div>\n", html);
```
<!-- endref -->

An alert is a [MarkdownAlertSyntax](cref:Meziantou.Framework.Markdown.MarkdownAlertSyntax):
[AlertKind](cref:Meziantou.Framework.Markdown.MarkdownAlertSyntax.AlertKind) gives the kind, as it is written, and
[Blocks](cref:Meziantou.Framework.Markdown.MarkdownAlertSyntax.Blocks) its content. The kind is case-insensitive, and a
kind of other letters, such as `[!EXAMPLE]`, is an alert without a title.

Only the block quotes of the document are alerts, not the ones in a list or another block quote, as on GitHub, unless
[AllowNestedAlerts](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AllowNestedAlerts) is set.

With [Bootstrap](31-Bootstrap.md), the alerts get the classes of the alerts of Bootstrap instead.
