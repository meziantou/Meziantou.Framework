---
title: Appending versions
description: Add a ?v= parameter computed from the content of each local file to the URLs of style sheets, scripts and images.
---

Browsers cache the style sheets, scripts and images of a page. `append-version` adds a version to their URLs, computed
from the content of each file: when a file changes, its URL changes, and browsers download it again.

## Append versions

This page references a style sheet, a script that already has a version, an image and an external page:

<!-- ref:../../tests/Meziantou.Framework.Html.Tool.Tests/Examples/AppendVersion/index.html;format=md-fenced-code-block;language=html -->
```html
<link rel="stylesheet" href="css/site.css">
<script src="js/app.js?v=1"></script>
<img src="images/logo.svg" alt="Logo">
<a href="https://example.com/">Home</a>
```
<!-- endref -->

`css/site.css`, `js/app.js` and `images/logo.svg` exist next to the page:

```bash
meziantou.html append-version --single-file index.html
```

The tool writes `Updated 3 nodes in 'index.html'`, and the page becomes:

<!-- ref:../../tests/Meziantou.Framework.Html.Tool.Tests/Examples/AppendVersion/index.expected.html;format=md-fenced-code-block;language=html -->
```html
<link rel="stylesheet" href="css/site.css?v=a6159d">
<script src="js/app.js?v=7fbe4e"></script>
<img src="images/logo.svg?v=0d1df5" alt="Logo">
<a href="https://example.com/">Home</a>
```
<!-- endref -->

The command has no other option than the [file selection](03-SelectingFiles.md) ones.

## Which URLs get a version

The tool reads the `src`, `href` and `poster` attributes of all the elements. A URL gets a version when it names a file
that exists, relative to the folder of the HTML file. The other URLs are not changed: external URLs, anchors
(`#top`), and the files that do not exist.

- The `href` of a link to another local page (`<a href="about.html">`) gets a version too.
- A root-relative URL (`/css/site.css`) is resolved from the root of the file system, not of your site, so it usually
  gets no version: use URLs relative to the page.
- The query string and the fragment do not take part in finding the file: `js/app.js?v=1#main` is `js/app.js`.

## The version

The version is the first six hexadecimal characters, in lowercase, of the SHA-512 hash of the content of the file. The
same content always gives the same version, so running the command again changes nothing.

- A URL without query string gets `?v=<version>`.
- A URL whose query string has a `v` parameter gets its value replaced; the other parameters are kept.
- A URL whose query string has no `v` parameter gets `&v=<version>` at its end.
- The fragment (`#…`) stays at the end of the URL.

A file is written only when at least one URL changed.
