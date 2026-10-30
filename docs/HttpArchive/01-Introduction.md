---
title: Introduction
description: A .NET model of the HAR 1.2 format, with parsing, writing, and conversion to HttpClient messages.
---

`Meziantou.Framework.HttpArchive` reads and writes HAR (HTTP Archive) 1.2 files: the JSON format that browsers and
HTTP tools use to export the requests a page or an application sent, with their responses and timings. The library
gives you a typed model of the format, and converts archived entries to `HttpRequestMessage` and
`HttpResponseMessage`.

## What you can do with it

- **Read an archive** from a string or a stream, synchronously or not, and walk its entries. See
  [Getting started](02-GettingStarted.md).
- **Create and write an archive**, to a string or a stream, indented or not. See
  [Reading and writing](03-ReadingAndWriting.md).
- **Keep the fields of other tools**: custom fields such as `_resourceType` are read and written back.
- **Handle incomplete archives**: an explicit `null` for any field still parses. See
  [Missing and null values](04-MissingAndNullValues.md).
- **Replay an entry** with `HttpClient`, or inspect it with the usual HTTP types: `ToHttpRequestMessage` and
  `ToHttpResponseMessage` rebuild the messages, with their headers, cookies and bodies. See
  [Converting entries](05-ConvertingEntries.md).
- **Get the bytes of a body**, whether the archive stored it as text or as base64.

> [!WARNING]
> **HAR files contain credentials.** An archive records `Authorization` headers, cookies, tokens and complete request
> bodies exactly as they were sent. The library does not redact anything. See
> [Sharing archives](06-SharingArchives.md).

## The model

`HarDocument` is the root of a file. Its types follow the objects of the HAR 1.2 format:

| Type | HAR object | Content |
| --- | --- | --- |
| `HarDocument` | root | The `Log`. |
| `HarLog` | `log` | The `Version`, the `Creator` and `Browser`, the `Pages` and the `Entries`. |
| `HarPage` | `pages[]` | A page load, its `Id`, `Title` and `PageTimings`. |
| `HarEntry` | `entries[]` | One request: `Request`, `Response`, `Cache`, `Timings`, `StartedDateTime`, `Time`, `Pageref`. |
| `HarRequest` | `request` | `Method`, `Url`, `HttpVersion`, `Headers`, `Cookies`, `QueryString`, `PostData`. |
| `HarResponse` | `response` | `Status`, `StatusText`, `HttpVersion`, `Headers`, `Cookies`, `Content`, `RedirectUrl`. |
| `HarContent` | `content` | The response body: `Text`, `Encoding`, `MimeType`, `Size`. |
| `HarPostData` | `postData` | The request body: `Text` or `Params`, and `MimeType`. |
| `HarTimings` | `timings` | The durations of the request phases, in milliseconds. |

Every object also has a `Comment` and an `ExtensionData` dictionary that holds the fields the format does not define.

## Namespace

Every type is in the `Meziantou.Framework.HttpArchive` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and read your first archive.
