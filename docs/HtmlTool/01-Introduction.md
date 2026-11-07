---
title: Introduction
description: A command-line tool that updates HTML files in place, for build and deployment scripts.
---

`Meziantou.Framework.Html.Tool` is a .NET tool, `meziantou.html`, that updates HTML files in place. Run it in a build
or a deployment script, after the files are generated and before they are published.

## What you can do with it

- **Replace values**: set the text of the elements or the values of the attributes that an XPath expression selects,
  such as a version number. See [Replacing values](04-ReplacingValues.md).
- **Append versions to URLs**: add a `?v=` parameter, computed from the content of the file, to the URLs of the style
  sheets, scripts and images, so that browsers download a file again when it changes. See
  [Appending versions](05-AppendingVersions.md).
- **Inline resources**: replace the references to style sheets, scripts and images with their content, to get a
  single-file page. See [Inlining resources](06-InliningResources.md).

Each command updates one file, or all the files that a glob pattern matches. See
[Selecting files](03-SelectingFiles.md).

## How it works

The tool parses each file with the HTML parser of
[Meziantou.Framework.WebPlatform](../WebPlatform/01-Introduction.md), as a browser does, finds the nodes to change,
and writes the file back. The patterns use the standard dialect of
[Meziantou.Framework.Globbing](../Globbing/01-Introduction.md).

The updated files are re-serialized in the standard HTML form: the formatting of the markup, such as the quotes of the
attributes or the self-closing tags, is not preserved. See [Output format and encoding](07-OutputFormatAndEncoding.md).

## Next steps

- [Getting started](02-GettingStarted.md): install the tool and update your first file.
- [Output format and encoding](07-OutputFormatAndEncoding.md): what the files look like after an update.
