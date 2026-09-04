# Meziantou.AspNetCore.Mvc

`Meziantou.AspNetCore.Mvc` provides useful TagHelpers for ASP.NET Core MVC and Razor Pages.

## Installation

```powershell
dotnet add package Meziantou.AspNetCore.Mvc
```

## Setup

Register the TagHelpers in your `_ViewImports.cshtml` file:

```razor
@addTagHelper *, Meziantou.AspNetCore.Mvc
```

## TagHelpers

### `show-if`

Conditionally renders an element.

```razor
<div show-if="@User.Identity?.IsAuthenticated == true">
    Welcome back!
</div>
```

### `inline-style`

Inlines a CSS file from `wwwroot` into a `<style>` element.

```razor
<inline-style href="css/site.css"></inline-style>
```

### `inline-script`

Inlines a JavaScript file from `wwwroot` into a `<script>` element.

```razor
<inline-script src="js/app.js"></inline-script>
```

### `inline-img`

Inlines an image from `wwwroot` as a base64 data URI.

```razor
<inline-img src="images/logo.png" alt="Company logo" />
```

### `render-on-page-load`

Defers rendering of non-critical content until the page loads. The `id` attribute is optional and only used
if you need to reference the `<noscript>` element yourself.

```razor
<render-on-page-load>
    <link rel="stylesheet" href="/css/non-critical.css" />
</render-on-page-load>
```

### `datetime-value`

Formats a `DateTimeOffset` value into an ISO 8601 `datetime` attribute on `<time>`, `<del>` or `<ins>`.
The value is normalized to UTC and written with the `Z` designator.

```razor
<time datetime-value="@Model.PublishedAt">Published</time>
<!-- Outputs: <time datetime="2024-01-15T10:30:45.123Z">Published</time> -->
```

Literal `datetime` attributes are left untouched, so existing markup keeps working:

```razor
<time datetime="2024-01-01">Jan 1</time>
```

## Additional resources

- [Loading stylesheets asynchronously using a TagHelper in ASP.NET Core](https://www.meziantou.net/loading-stylesheets-asynchronously-using-a-taghelper-in-asp-net-core.htm)
