---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **One way.** The library converts ADF to Markdown. It does not convert Markdown, HTML or the older wiki markup of
  Jira to ADF: build the document with [the object model](04-ObjectModel.md#build-a-document) instead.
- **No API client.** The library does not call the Atlassian APIs. Download the documents, and the media they
  reference, with your own HTTP client; see [Media and mentions](03-ConvertingToMarkdown.md#media-and-mentions).
- **Lossy conversion.** Colors, underline, alignment, annotations and the other marks without a Markdown equivalent are
  dropped, and pipe tables lose cell spans; see [Other nodes and marks](03-ConvertingToMarkdown.md#other-nodes-and-marks).
- **No schema validation.** Parsing accepts any tree under a `doc` root, and building a document does not check that a
  node is allowed where you put it.
- **Known nodes keep only their modeled attributes.** Writing a parsed document back drops the attributes the classes
  do not have; unknown nodes and marks are kept whole.
