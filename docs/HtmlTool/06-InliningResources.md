---
title: Inlining resources
description: Replace the references to local scripts, style sheets and images with their content, to get a single-file page.
---

`inline-resources` copies the local files that a page references into the page itself. The result is one file that
works without its folder: a report to send by email, a page to archive, or an offline page.

## Inline the resources of a page

This page references a style sheet, a script and an image that exist next to it:

<!-- ref:../../tests/Meziantou.Framework.Html.Tool.Tests/Examples/InlineResources/index.html;format=md-fenced-code-block;language=html -->
```html
<link rel="stylesheet" href="site.css">
<script src="app.js"></script>
<img src="logo.svg" alt="Logo">
```
<!-- endref -->

```bash
meziantou.html inline-resources --single-file index.html --resource-patterns "*.css"
```

The tool writes `Updated 3 nodes in 'index.html'`, and the page becomes:

<!-- ref:../../tests/Meziantou.Framework.Html.Tool.Tests/Examples/InlineResources/index.expected.html;format=md-fenced-code-block;language=html -->
```html
<style>body { margin: 0; }
</style>
<script>console.log("Hello");
</script>
<img src="data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxNiIgaGVpZ2h0PSIxNiI+PGNpcmNsZSBjeD0iOCIgY3k9IjgiIHI9IjgiLz48L3N2Zz4K" alt="Logo">
```
<!-- endref -->

- A `script` element loses its `src` attribute and gets the content of the file as its text.
- A `link` element is replaced by a `style` element that holds the content of the file. Its other attributes, such as
  `media`, are not kept.
- Any other element gets a `data:` URL, with the content of the file encoded in base64 and the media type of its
  extension (`application/octet-stream` for an unknown extension).

The script and the style sheet are copied as they are, their final line break included.

## Options

| Option | Description |
| --- | --- |
| `--resource-patterns <patterns>` | Required. Its value is not used by the current version: see below. |
| `--single-file`, `--file-pattern`, `--root-directory` | The files to update: see [Selecting files](03-SelectingFiles.md). |

`--resource-patterns` is required, and takes one or more values, but the command does not read them: every local file
that the page references is inlined, whatever its name. In the example, the script and the image are inlined although
the pattern is `*.css`.

## Which references are inlined

As for [appending versions](05-AppendingVersions.md#which-urls-get-a-version), the tool reads the `src`, `href` and
`poster` attributes of all the elements, and inlines the files that exist, relative to the folder of the HTML file.
External URLs, anchors and missing files are not changed. A file is written only when at least one reference was
inlined.

## Pitfalls

- Every `link` element that references a local file becomes a `style` element, whatever its `rel`: a
  `<link rel="icon" href="favicon.ico">` is replaced by a `style` element that holds the content of the icon, read as
  text. Add such links after you run the command.
- A link to another local page (`<a href="about.html">`) gets a `data:` URL with the content of that page.
- The URLs inside the inlined files are not changed: a style sheet that references `../fonts/font.woff2` resolves it
  from the page, not from the folder of the style sheet.
- A large image makes a large page: base64 is a third larger than the file.
