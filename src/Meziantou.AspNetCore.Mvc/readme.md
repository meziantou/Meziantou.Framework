# Meziantou.AspNetCore.Mvc

`Meziantou.AspNetCore.Mvc` provides useful TagHelpers for ASP.NET Core MVC and Razor Pages.

## Installation

```powershell
dotnet add package Meziantou.AspNetCore.Mvc
```

## Setup

Register the TagHelpers in your `_ViewImports.cshtml` file:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/_ViewImports.cshtml;format=md-fenced-code-block -->
```cshtml
@addTagHelper *, Meziantou.AspNetCore.Mvc
```
<!-- endref -->

## TagHelpers

### `show-if`

Conditionally renders an element.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/ShowIf.cshtml;format=md-fenced-code-block -->
```cshtml
<div show-if="@User.Identity?.IsAuthenticated == true">
    Welcome back!
</div>
```
<!-- endref -->

### `inline-style`

Inlines a CSS file from `wwwroot` into a `<style>` element.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/InlineStyle.cshtml;format=md-fenced-code-block -->
```cshtml
<inline-style href="css/site.css"></inline-style>
```
<!-- endref -->

### `inline-script`

Inlines a JavaScript file from `wwwroot` into a `<script>` element.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/InlineScript.cshtml;format=md-fenced-code-block -->
```cshtml
<inline-script src="js/app.js"></inline-script>
```
<!-- endref -->

### `inline-img`

Inlines an image from `wwwroot` as a base64 data URI.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/InlineImg.cshtml;format=md-fenced-code-block -->
```cshtml
<inline-img src="images/logo.png" alt="Company logo" />
```
<!-- endref -->

### `render-on-page-load`

Defers rendering of non-critical content until the page loads. The `id` attribute is optional and only used
if you need to reference the `<noscript>` element yourself.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/RenderOnPageLoad.cshtml;format=md-fenced-code-block -->
```cshtml
<render-on-page-load>
    <link rel="stylesheet" href="/css/non-critical.css" />
</render-on-page-load>
```
<!-- endref -->

### `datetime-value`

Formats a `DateTimeOffset` value into an ISO 8601 `datetime` attribute on `<time>`, `<del>` or `<ins>`.
The value is normalized to UTC and written with the `Z` designator.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/DateTimeValue.cshtml;format=md-fenced-code-block -->
```cshtml
@model Meziantou.AspNetCore.Samples.Article

<time datetime-value="@Model.PublishedAt">Published</time>
<!-- Outputs: <time datetime="2024-01-15T10:30:45.123Z">Published</time> -->
```
<!-- endref -->

Literal `datetime` attributes are left untouched, so existing markup keeps working:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/Views/MvcReadme/LiteralDateTime.cshtml;format=md-fenced-code-block -->
```cshtml
<time datetime="2024-01-01">Jan 1</time>
```
<!-- endref -->

## Additional resources

- [Loading stylesheets asynchronously using a TagHelper in ASP.NET Core](https://www.meziantou.net/loading-stylesheets-asynchronously-using-a-taghelper-in-asp-net-core.htm)
