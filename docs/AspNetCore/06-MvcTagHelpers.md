---
title: MVC tag helpers
description: Render elements conditionally, inline CSS, scripts and images from wwwroot, defer content, and write datetime attributes in MVC views and Razor Pages.
---

`Meziantou.AspNetCore.Mvc` adds tag helpers to MVC views and Razor Pages.

<InstallNuGetPackage Id="Meziantou.AspNetCore.Mvc" />

Register them in `_ViewImports.cshtml`:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/_ViewImports.cshtml;format=md-fenced-code-block -->
```cshtml
@addTagHelper *, Meziantou.AspNetCore.Mvc
```
<!-- endref -->

| Tag helper | Applies to | What it does |
| --- | --- | --- |
| `show-if` | Any element with a `show-if` attribute | Renders the element only when the condition is `true` |
| `<inline-style>` | `<inline-style href="...">` | Replaces the element with a `<style>` that contains the file |
| `<inline-script>` | `<inline-script src="...">` | Replaces the element with a `<script>` that contains the file |
| `<inline-img>` | `<inline-img src="...">` | Replaces the element with an `<img>` whose `src` is a `data:` URI |
| `<render-on-page-load>` | `<render-on-page-load>` | Renders its content once the page has loaded |
| `datetime-value` | `<time>`, `<del>` and `<ins>` | Writes a `DateTimeOffset` as an ISO 8601 `datetime` attribute in UTC |

## Render an element conditionally

`show-if` renders the element when its value is `true` and removes it, with its content, otherwise:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/ShowIf.cshtml;format=md-fenced-code-block -->
```cshtml
<div show-if="@User.Identity?.IsAuthenticated == true">
    Welcome back!
</div>
```
<!-- endref -->

The `show-if` attribute itself is not rendered.

## Inline a file from wwwroot

The inline tag helpers read a file of the web root (`IWebHostEnvironment.WebRootFileProvider`, usually `wwwroot`) and
put its content in the page, which saves a request for small, critical files:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/InlineStyle.cshtml;format=md-fenced-code-block -->
```cshtml
<inline-style href="css/site.css"></inline-style>
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/InlineScript.cshtml;format=md-fenced-code-block -->
```cshtml
<inline-script src="js/app.js"></inline-script>
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/InlineImg.cshtml;format=md-fenced-code-block -->
```cshtml
<inline-img src="images/logo.png" alt="Company logo" />
```
<!-- endref -->

- `<inline-style>` renders `<style>` with the text of the file, and `<inline-script>` renders `<script>`. A `</style>`
  or `</script>` sequence in the file is escaped, so the file cannot close the element early.
- `<inline-img>` renders `<img>` with a Base64 `data:` URI. The content type comes from the file extension, and is
  `application/octet-stream` when the extension is unknown. The other attributes, such as `alt`, are kept.
- The content is cached in the application's `IMemoryCache`, and the cache entry is removed when the file changes.
  Entries have a size, so the tag helpers also work when `MemoryCacheOptions.SizeLimit` is set.
- When the file does not exist, nothing is rendered and a warning is logged. The missing path is watched too, so the
  file is picked up when it is created.

Inline only small files: the content is sent with every page instead of being cached by the browser once.

## Defer content until the page has loaded

`<render-on-page-load>` keeps its content out of the initial rendering, for instance a stylesheet or a script that is
not needed to display the page:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/RenderOnPageLoad.cshtml;format=md-fenced-code-block -->
```cshtml
<render-on-page-load>
    <link rel="stylesheet" href="/css/non-critical.css" />
</render-on-page-load>
```
<!-- endref -->

The content is rendered in a `<noscript>` element, followed by a small script that adds it to the page on the `load`
event (immediately when the page is already loaded). Browsers without JavaScript render the `<noscript>` content
directly. The `id` attribute is optional and sets the `id` of the `<noscript>` element, for when you need to reference
it:

<!-- ref:../../samples/Meziantou.AspNetCore.Mvc.Samples/Views/Examples/RenderOnPageLoad.cshtml;format=md-fenced-code-block -->
```cshtml
@* Defer loading of non-critical stylesheet *@
<render-on-page-load>
    <link rel="stylesheet" href="/css/non-critical.css" />
</render-on-page-load>

@* Defer loading with custom ID *@
<render-on-page-load id="analytics-scripts">
    <script src="/js/analytics.js"></script>
</render-on-page-load>
```
<!-- endref -->

## Write a datetime attribute

`datetime-value` writes a `DateTimeOffset` in the `datetime` attribute of `<time>`, `<del>` or `<ins>`:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/DateTimeValue.cshtml;format=md-fenced-code-block -->
```cshtml
@model Meziantou.AspNetCore.Samples.Article

<time datetime-value="@Model.PublishedAt">Published</time>
<!-- Outputs: <time datetime="2024-01-15T10:30:45.123Z">Published</time> -->
```
<!-- endref -->

The value is converted to UTC and written with the `Z` designator, with milliseconds
(`yyyy-MM-ddTHH:mm:ss.fffZ`). A `datetime` without a time zone designator is read as local time by the reader, which
would shift it by their UTC offset. When the value is `null`, the `datetime` attribute is removed.

Literal `datetime` attributes are left unchanged:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/LiteralDateTime.cshtml;format=md-fenced-code-block -->
```cshtml
<time datetime="2024-01-01">Jan 1</time>
```
<!-- endref -->

## Writing your own inline tag helper

The inline tag helpers derive from the public [InlineTagHelper](cref:Meziantou.AspNetCore.Mvc.TagHelpers.InlineTagHelper) class. Derive from it to inline files in another
element: its protected [GetFileContentAsync](cref:Meziantou.AspNetCore.Mvc.TagHelpers.InlineTagHelper.GetFileContentAsync) and [GetFileContentBase64Async](cref:Meziantou.AspNetCore.Mvc.TagHelpers.InlineTagHelper.GetFileContentBase64Async) methods read a web root file as text or as
Base64, with the same cache and file watching.
