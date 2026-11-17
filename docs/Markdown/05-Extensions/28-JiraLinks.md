---
title: Jira links
description: Turn references to Jira issues, such as PROJ-123, into links to the issues.
---

Release notes, commit messages and specifications often mention issues by their key.
[JiraLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.JiraLinks) turns a reference to an issue, such as
`PROJ-123`, into a link to the issue on your Jira site.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_JiraLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.JiraLinks,
    JiraLinks = new MarkdownJiraLinkOptions { BaseUrl = "https://example.atlassian.net" },
};
var tree = MarkdownSyntaxTree.ParseText("Fixed in (PROJ-123), not in xPROJ-1.\n", options);

var link = tree.GetRoot().DescendantNodes().OfType<MarkdownJiraLinkSyntax>().Single();
Assert.Equal("PROJ", link.ProjectKey);
Assert.Equal("123", link.IssueNumber);
Assert.Equal("<p>Fixed in (<a href=\"https://example.atlassian.net/browse/PROJ-123\" target=\"_blank\">PROJ-123</a>), not in xPROJ-1.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

A reference is a link after whitespace or `(` and before whitespace or `)`. It is a
[MarkdownJiraLinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownJiraLinkSyntax), whose
[ProjectKey](cref:Meziantou.Framework.Markdown.MarkdownJiraLinkSyntax.ProjectKey) and
[IssueNumber](cref:Meziantou.Framework.Markdown.MarkdownJiraLinkSyntax.IssueNumber) give the parts of the key.

[MarkdownParseOptions.JiraLinks](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.JiraLinks) gives the URL of the
site, and whether the links open in a new window.
