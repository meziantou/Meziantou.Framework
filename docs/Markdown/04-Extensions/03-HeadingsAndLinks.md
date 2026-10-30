---
title: Headings and links
description: Give headings an identifier, link text that looks like a URL, add rel values, link Jira issues and embed videos.
---

## Heading identifiers

`UseAutoIdentifiers` gives each heading an `id` computed from its text, so that a URL can link to a section
(`page.html#getting-started`). A heading with the same text as a previous one gets a number. A link whose text is the
text of a heading links to that heading, without a URL:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseAutoIdentifiers;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAutoIdentifiers()
    .Build();

var html = MarkdownConverter.ToHtml("# Getting started\n\n## Notes\n\n## Notes\n\nSee [Getting started].", pipeline);
Assert.Equal("""
    <h1 id="getting-started">Getting started</h1>
    <h2 id="notes">Notes</h2>
    <h2 id="notes-1">Notes</h2>
    <p>See <a href="#getting-started">Getting started</a>.</p>

    """.ReplaceLineEndings("\n"), html);
```
<!-- endref -->

A `{#id}` [attribute](06-GenericAttributes.md) replaces the computed identifier.

The `AutoIdentifierOptions` flags (namespace [Meziantou.Framework.Markdown.Extensions.AutoIdentifiers](cref:Meziantou.Framework.Markdown.Extensions.AutoIdentifiers)) change how the
identifier is computed:

| Option | Effect |
| --- | --- |
| `Default` | `AutoLink` and `AllowOnlyAscii` |
| `AutoLink` | Links `[Heading text]` to the heading |
| `AllowOnlyAscii` | Removes the accents and the other non-ASCII characters (`Été` becomes `ete`, `日本語 text` becomes `text`) |
| `GitHub` | Computes the identifiers as GitHub does, which keeps the non-ASCII letters |
| `None` | None of the above |

Use `GitHub` when the documents are also displayed on GitHub, so that the links to their sections work on both:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseAutoIdentifiersGitHub;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "# Été 2024!";

var defaultPipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers().Build();
Assert.Equal("<h1 id=\"ete-2024\">Été 2024!</h1>\n", MarkdownConverter.ToHtml(markdown, defaultPipeline));

var gitHubPipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).Build();
Assert.Equal("<h1 id=\"été-2024\">Été 2024!</h1>\n", MarkdownConverter.ToHtml(markdown, gitHubPipeline));
```
<!-- endref -->

`UseAdvancedExtensions` enables this extension with the default options. To use other options with it, call
`UseAutoIdentifiers` first; see [Enable the common extensions at once](01-Overview.md#enable-the-common-extensions-at-once).

## Automatic links

CommonMark only links URLs written between `<` and `>`. `UseAutoLinks` also links the text that starts with `http://`,
`https://`, `ftp://`, `mailto:` or `www.`. A `www.` link gets the `http://` scheme, or `https://` with
`UseHttpsForWWWLinks`:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestAutoLinks.cs;region=MarkdownExtensions_UseAutoLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "Visit www.example.com or https://example.org.";

var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
Assert.Equal("<p>Visit <a href=\"http://www.example.com\">www.example.com</a> or <a href=\"https://example.org\">https://example.org</a>.</p>\n", MarkdownConverter.ToHtml(markdown, pipeline));

var httpsPipeline = new MarkdownPipelineBuilder().UseAutoLinks(new AutoLinkOptions { UseHttpsForWWWLinks = true }).Build();
Assert.Equal("<p>Visit <a href=\"https://www.example.com\">www.example.com</a> or <a href=\"https://example.org\">https://example.org</a>.</p>\n", MarkdownConverter.ToHtml(markdown, httpsPipeline));
```
<!-- endref -->

The options of `AutoLinkOptions` (namespace [Meziantou.Framework.Markdown.Extensions.AutoLinks](cref:Meziantou.Framework.Markdown.Extensions.AutoLinks)):

| Option | Default | Effect |
| --- | --- | --- |
| `UseHttpsForWWWLinks` | `false` | Prefixes `www.` links with `https://` instead of `http://` |
| `AllowDomainWithoutPeriod` | `false` | Links domains without a period, such as `https://localhost` |
| `OpenInNewWindow` | `false` | Adds `target="_blank"` to the links |
| `ValidPreviousCharacters` | `*_~(` | The characters, other than whitespace, that can precede a link |

## Rel attributes

`UseReferralLinks` adds `rel` values to every link, for instance to tell search engines not to follow the links of user
content, or to keep the linked pages from accessing yours:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestReferralLinks.cs;region=MarkdownExtensions_UseReferralLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseReferralLinks("nofollow", "noopener", "noreferrer")
    .Build();

var html = MarkdownConverter.ToHtml("[Example](https://example.com)", pipeline);
Assert.Equal("<p><a href=\"https://example.com\" rel=\"nofollow noopener noreferrer\">Example</a></p>\n", html);
```
<!-- endref -->

## Jira links

`UseJiraLinks` links the references to Jira issues, such as `PROJ-123`. The project key is made of uppercase ASCII
letters and digits and starts with a letter; it is followed by `-` and the issue number. The reference must be preceded
by a space, a `(` or the start of the text, and followed by a space, a `)` or the end of the text:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseJiraLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseJiraLinks(new JiraLinkOptions("https://jira.example.com"))
    .Build();

var html = MarkdownConverter.ToHtml("Fixed by PROJ-123 (see also PROJ-99)", pipeline);
Assert.Equal("<p>Fixed by <a href=\"https://jira.example.com/browse/PROJ-123\" target=\"_blank\">PROJ-123</a> (see also <a href=\"https://jira.example.com/browse/PROJ-99\" target=\"_blank\">PROJ-99</a>)</p>\n", html);
```
<!-- endref -->

The URL is `BaseUrl`, then `BasePath` (`/browse` by default), then the reference. Jira links open in a new window
(`target="_blank"`) unless `OpenInNewWindow` is `false`.

## Embedded media

`UseMediaLinks` turns the images whose URL is a video or an audio file into `<video>` and `<audio>` players, and the
images that link to YouTube, Vimeo, Yandex Music or Odnoklassniki into the player of the site, in an `<iframe>`:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestMediaLinks.cs;region=MarkdownExtensions_UseMediaLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseMediaLinks(new MediaOptions { Width = "640", Height = "360" })
    .Build();

var video = MarkdownConverter.ToHtml("![Demo](https://example.com/demo.mp4)", pipeline);
Assert.Equal("<p><video width=\"640\" height=\"360\" controls=\"\"><source type=\"video/mp4\" src=\"https://example.com/demo.mp4\"></source></video></p>\n", video);

var youTube = MarkdownConverter.ToHtml("![Talk](https://www.youtube.com/watch?v=dQw4w9WgXcQ)", pipeline);
Assert.Equal("<p><iframe src=\"https://www.youtube.com/embed/dQw4w9WgXcQ\" class=\"youtube\" width=\"640\" height=\"360\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n", youTube);
```
<!-- endref -->

The options of `MediaOptions` (namespace [Meziantou.Framework.Markdown.Extensions.MediaLinks](cref:Meziantou.Framework.Markdown.Extensions.MediaLinks)):

| Option | Default | Effect |
| --- | --- | --- |
| `Width`, `Height` | `500`, `281` | The size of the players |
| `AddControlsProperty` | `true` | Adds the `controls` attribute to `<video>` and `<audio>` |
| `Class` | | The class of the players |
| `ExtensionToMimeType` | Common audio and video formats | The file extensions that are played, and their MIME type |
| `Hosts` | The sites above | The sites whose links become an `<iframe>`; `HostProviderBuilder.Create` creates one for another site |

## Non-ASCII URLs

By default, the non-ASCII characters of URLs are percent-encoded. `UseNonAsciiNoEscape` writes them as they are:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseNonAsciiNoEscape;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "[Summer](https://example.com/été)";

Assert.Equal("<p><a href=\"https://example.com/%C3%A9t%C3%A9\">Summer</a></p>\n", MarkdownConverter.ToHtml(markdown));

var pipeline = new MarkdownPipelineBuilder().UseNonAsciiNoEscape().Build();
Assert.Equal("<p><a href=\"https://example.com/été\">Summer</a></p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
```
<!-- endref -->
