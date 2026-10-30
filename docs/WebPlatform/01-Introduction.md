---
title: Introduction
description: A browser engine for .NET, in managed code: HTML, CSS, the DOM, layout, scripts, screenshots and PDF.
---

`Meziantou.Framework.WebPlatform` implements the web platform in .NET. It parses HTML, XML, SVG, MathML and CSS exactly
as a browser does, and exposes the DOM API that the scripts of a page use, with .NET naming. A layout engine computes the
geometry of the pages, and the companion packages run their scripts, take screenshots, print them to PDF and automate them
with Playwright.

## What you can do with it

- **Parse and query HTML**: parse a page as a browser does, whatever its errors, then query it with CSS selectors, XPath
  and LINQ. See [HTML and the DOM](03-HtmlAndDom/01-Overview.md).
- **Change and write HTML**: create and move elements, insert untrusted markup safely, and write the document back,
  minified or indented. See [Modifying documents](03-HtmlAndDom/04-ModifyingDocuments.md) and
  [Sanitizing HTML](03-HtmlAndDom/06-SanitizingHtml.md).
- **Work with CSS**: read the computed style of an element, edit style sheets, and format CSS. See
  [CSS](04-Css/01-Overview.md).
- **Load pages**: navigate to a URL with its style sheets, images and frames, with cookies and storage, emulate a device,
  read the layout and inject the input of a user. See [Loading pages](05-LoadingPages/01-Overview.md).
- **Run the scripts of the pages** with a JavaScript engine written in .NET, and WebAssembly modules. See
  [Scripting](06-Scripting/01-Overview.md).
- **Render pages**: take screenshots of pages and elements, print them to images or PDF, draw on canvases. See
  [Rendering](07-Rendering/01-Overview.md).
- **Automate pages with Playwright**: the Playwright clients for .NET, Node.js, Python and Java drive the engine as they
  drive Chromium. See [Playwright](08-Playwright/01-Overview.md).

## Packages

The library is split into packages, so that an application only references what it uses.

| Package | Adds |
| --- | --- |
| `Meziantou.Framework.WebPlatform` | The parsers, the DOM, CSS, the layout, the browsing contexts and their event loop |
| `Meziantou.Framework.WebPlatform.JavaScript` | A script engine: runs the scripts of the pages with Meziantou.Framework.JavaScriptEngine |
| `Meziantou.Framework.WebPlatform.Wasmtime` | A WebAssembly runtime for the scripts, based on [Wasmtime](https://wasmtime.dev/) (a native library) |
| `Meziantou.Framework.WebPlatform.Imaging` | A rendering backend: screenshots, pages printed as images, 2D canvases and image decoding, with Meziantou.Framework.Imaging |
| `Meziantou.Framework.WebPlatform.PdfRendering` | Printing to PDF, with Meziantou.Framework.Pdf |
| `Meziantou.Framework.WebPlatform.Playwright` | A Playwright server whose browser is this engine |
| `Meziantou.Framework.WebPlatform.Playwright.Tool` | A .NET tool that runs the Playwright server from the command line |

## Principles

- **Behaves as a browser.** The parsers, the DOM, the cascade and the layout follow the WHATWG and W3C specifications: a
  page gives the same tree, the same styles and the same events as in a browser.
- **Everything is opt-in.** A parsed document is only a tree. Nothing is fetched without a document loader, no script
  runs without a script engine, and no pixel is drawn without a rendering backend. You decide what a page can do.
- **Same pixels everywhere.** The rendering is fully managed and the default fonts are part of the library: the same page
  gives the same screenshot on Windows, Linux and macOS.
- **The web API, with .NET names.** Every member that maps a member of the web API has the
  `[IdlMember(name, specUrl)]` attribute. The names follow the .NET guidelines: acronyms of 3 letters or more are
  PascalCased and acronyms of 2 letters stay upper case (`innerHTML` is `InnerHtml`, `toJSON` is `ToJson`,
  `getElementsByTagNameNS` is `GetElementsByTagNameNS`, `HTMLBRElement` is `HtmlBRElement`).

## Namespaces

| Namespace | Contains |
| --- | --- |
| `Meziantou.Framework.WebPlatform` | The parsers: `HtmlDocumentParser`, `XmlDocumentParser` and their options |
| `Meziantou.Framework.WebPlatform.Dom` | Browsing contexts, documents, nodes and elements, events, the document loader, cookies |
| `Meziantou.Framework.WebPlatform.Formatting` | The HTML and CSS formatters |
| `Meziantou.Framework.WebPlatform.Css.Om` | Style sheets, rules and declarations (the CSSOM) |
| `Meziantou.Framework.WebPlatform.Css.Selectors` | The parsed selectors |
| `Meziantou.Framework.WebPlatform.Css.TypedOm` | CSS values as objects |
| `Meziantou.Framework.WebPlatform.JavaScript` | The JavaScript script engine |
| `Meziantou.Framework.WebPlatform.Imaging` | The rendering backend, the screenshots and the printing to images |
| `Meziantou.Framework.WebPlatform.PdfRendering` | The printing to PDF |
| `Meziantou.Framework.WebPlatform.Playwright` | The Playwright server |

## Next steps

- [Getting started](02-GettingStarted.md): install the package, parse a page, read its styles and take a screenshot.
- [Loading pages](05-LoadingPages/01-Overview.md): the browsing context, which turns a document into a page.
- [Limitations](09-Limitations.md): what the engine does not do.
