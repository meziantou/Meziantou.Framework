---
title: Replacing values
description: Set the text of elements and the values of attributes selected by an XPath expression, such as a version number.
---

`replace-value` sets the nodes that an XPath expression selects to a new value. Use it to write a version number, a
build date or a URL into pages that are already generated.

## Replace an attribute and a text

This page has a placeholder version in a `meta` element and in a `span`:

<!-- ref:../../tests/Meziantou.Framework.Html.Tool.Tests/Examples/ReplaceValue/index.html;format=md-fenced-code-block;language=html -->
```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="version" content="0.0.0">
  <title>My app</title>
</head>
<body>
  <p>Version <span id="version">dev</span></p>
</body>
</html>
```
<!-- endref -->

`--xpath` selects both, with the `|` union of XPath, and `--new-value` gives the value:

```bash
meziantou.html replace-value --single-file index.html --xpath "//meta[@name='version']/@content | //span[@id='version']" --new-value 1.2.0
```

The tool writes `Updated 2 nodes in 'index.html'`, and the page becomes:

<!-- ref:../../tests/Meziantou.Framework.Html.Tool.Tests/Examples/ReplaceValue/index.expected.html;format=md-fenced-code-block;language=html -->
```html
<!DOCTYPE html><html lang="en"><head>
  <meta charset="utf-8">
  <meta name="version" content="1.2.0">
  <title>My app</title>
</head>
<body>
  <p>Version <span id="version">1.2.0</span></p>


</body></html>
```
<!-- endref -->

The values are replaced, and the whole page is written again in the standard HTML form: the line break after the
doctype is removed, and the whitespace after `</body>` moves into the body. See
[Output format and encoding](07-OutputFormatAndEncoding.md).

## Options

| Option | Description |
| --- | --- |
| `--xpath <xpath>` | The XPath expression that selects the nodes to replace. Required. |
| `--new-value <value>` | The new value of the nodes. Required. |
| `--single-file`, `--file-pattern`, `--root-directory` | The files to update: see [Selecting files](03-SelectingFiles.md). |

## What the expression can select

The expression is an XPath 1.0 expression, evaluated as `document.evaluate()` does in a browser: see
[XPath](../WebPlatform/03-HtmlAndDom/03-QueryingDocuments.md#xpath). Each selected node gets the new value as its
text content:

| Selected node | Example | Result |
| --- | --- | --- |
| An attribute | `//html/@lang` | The value of the attribute is replaced. |
| A text node | `//title/text()` | The text is replaced. |
| An element | `//span[@id='version']` | The content of the element, its child elements included, is replaced by the text. |

The new value is text, not markup: a `<b>` in the value stays the text `<b>`, escaped in the file, and never adds an
element. To set the text of an element that has child elements without removing them, select one of its text nodes.

## Pitfalls

- A file is written again even when the expression selects no node (`Updated 0 nodes`): its markup is re-serialized.
- Quote the expression for your shell. In a shell that reads `'` and `"`, wrap it in `"` and use `'` for the strings of
  the expression, as in the example.
- An attribute that does not exist cannot be selected, so it is not created: give it a placeholder value in the source.
