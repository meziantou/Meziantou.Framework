---
title: Output format and encoding
description: How the tool writes the files it updates, documents and fragments, and how it keeps their encoding.
---

## The markup is re-serialized

The tool does not edit the text of a file: it parses the file, changes the nodes, and writes the whole tree again, as
a browser serializes a document (the `OuterHtml` of the DOM). The content is the same, but the formatting of the markup
is not preserved:

- the attribute values are written between double quotes: `href='a.css'` becomes `href="a.css"`;
- the void elements have no slash: `<link />` becomes `<link>`;
- the character references that do not need escaping are written as characters, and the ones that do are written in
  their standard form (`&amp;`, `&lt;`...);
- the elements that the parser adds are written, such as a missing `tbody` of a table;
- the whitespace between the elements stays, except where the parser moves it: the line break after the doctype is
  removed, and the whitespace after `</body>` moves into the body.

[Replacing values](04-ReplacingValues.md) shows a page before and after. To format the output, such as to indent or
minify it, run your formatter after the tool. [Serializing and formatting](../WebPlatform/03-HtmlAndDom/05-SerializingAndFormatting.md)
describes the serialization of WebPlatform.

## Documents and fragments

A file that contains a doctype, or an `html`, `head` or `body` tag, is a document: it is written with its `html`,
`head` and `body` elements, which the parser adds when one of them is missing. Any other file, such as a partial view
or a template part, is a fragment: it is written without them, so a fragment stays a fragment.

## Encoding

The tool reads each file with the encoding that the HTML parser finds, as a browser does: from the byte order mark, the
`<meta charset>` element, or the content itself (see
[Parse bytes and streams](../WebPlatform/03-HtmlAndDom/02-ParsingDocuments.md#parse-bytes-and-streams)). It writes
the file back with the same encoding. A UTF-8 file keeps its byte order mark when it had one, and gets none otherwise.

## When files are written

| Command | The file is written |
| --- | --- |
| `replace-value` | Always, even when the expression selects no node |
| `append-version` | When at least one URL gets a version |
| `inline-resources` | When at least one reference is inlined |

A file that is written is re-serialized as a whole, so even a file whose values did not change can get a different
markup the first time.
