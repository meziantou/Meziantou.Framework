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
- **Replay an entry** with `HttpClient`, or inspect it with the usual HTTP types: [ToHttpRequestMessage](cref:Meziantou.Framework.HttpArchive.HarEntryExtensions.ToHttpRequestMessage) and
  [ToHttpResponseMessage](cref:Meziantou.Framework.HttpArchive.HarEntryExtensions.ToHttpResponseMessage) rebuild the messages, with their headers, cookies and bodies. See
  [Converting entries](05-ConvertingEntries.md).
- **Get the bytes of a body**, whether the archive stored it as text or as base64.

> [!WARNING]
> **HAR files contain credentials.** An archive records `Authorization` headers, cookies, tokens and complete request
> bodies exactly as they were sent. The library does not redact anything. See
> [Sharing archives](06-SharingArchives.md).

## The model

[HarDocument](cref:Meziantou.Framework.HttpArchive.HarDocument) is the root of a file. Its types follow the objects of the HAR 1.2 format:

| Type | HAR object | Content |
| --- | --- | --- |
| [HarDocument](cref:Meziantou.Framework.HttpArchive.HarDocument) | root | The [Log](cref:Meziantou.Framework.HttpArchive.HarDocument.Log). |
| [HarLog](cref:Meziantou.Framework.HttpArchive.HarLog) | `log` | The [Version](cref:Meziantou.Framework.HttpArchive.HarLog.Version), the [Creator](cref:Meziantou.Framework.HttpArchive.HarLog.Creator) and [Browser](cref:Meziantou.Framework.HttpArchive.HarLog.Browser), the [Pages](cref:Meziantou.Framework.HttpArchive.HarLog.Pages) and the [Entries](cref:Meziantou.Framework.HttpArchive.HarLog.Entries). |
| [HarPage](cref:Meziantou.Framework.HttpArchive.HarPage) | `pages[]` | A page load, its [Id](cref:Meziantou.Framework.HttpArchive.HarPage.Id), [Title](cref:Meziantou.Framework.HttpArchive.HarPage.Title) and [PageTimings](cref:Meziantou.Framework.HttpArchive.HarPage.PageTimings). |
| [HarEntry](cref:Meziantou.Framework.HttpArchive.HarEntry) | `entries[]` | One request: [Request](cref:Meziantou.Framework.HttpArchive.HarEntry.Request), [Response](cref:Meziantou.Framework.HttpArchive.HarEntry.Response), [Cache](cref:Meziantou.Framework.HttpArchive.HarEntry.Cache), [Timings](cref:Meziantou.Framework.HttpArchive.HarEntry.Timings), [StartedDateTime](cref:Meziantou.Framework.HttpArchive.HarEntry.StartedDateTime), [Time](cref:Meziantou.Framework.HttpArchive.HarEntry.Time), [Pageref](cref:Meziantou.Framework.HttpArchive.HarEntry.Pageref). |
| [HarRequest](cref:Meziantou.Framework.HttpArchive.HarRequest) | `request` | [Method](cref:Meziantou.Framework.HttpArchive.HarRequest.Method), [Url](cref:Meziantou.Framework.HttpArchive.HarRequest.Url), [HttpVersion](cref:Meziantou.Framework.HttpArchive.HarRequest.HttpVersion), `Headers`, [Cookies](cref:Meziantou.Framework.HttpArchive.HarRequest.Cookies), [QueryString](cref:Meziantou.Framework.HttpArchive.HarRequest.QueryString), [PostData](cref:Meziantou.Framework.HttpArchive.HarRequest.PostData). |
| [HarResponse](cref:Meziantou.Framework.HttpArchive.HarResponse) | `response` | [Status](cref:Meziantou.Framework.HttpArchive.HarResponse.Status), [StatusText](cref:Meziantou.Framework.HttpArchive.HarResponse.StatusText), [HttpVersion](cref:Meziantou.Framework.HttpArchive.HarResponse.HttpVersion), `Headers`, [Cookies](cref:Meziantou.Framework.HttpArchive.HarResponse.Cookies), [Content](cref:Meziantou.Framework.HttpArchive.HarResponse.Content), [RedirectUrl](cref:Meziantou.Framework.HttpArchive.HarResponse.RedirectUrl). |
| [HarContent](cref:Meziantou.Framework.HttpArchive.HarContent) | `content` | The response body: `Text`, [Encoding](cref:Meziantou.Framework.HttpArchive.HarContent.Encoding), [MimeType](cref:Meziantou.Framework.HttpArchive.HarContent.MimeType), [Size](cref:Meziantou.Framework.HttpArchive.HarContent.Size). |
| [HarPostData](cref:Meziantou.Framework.HttpArchive.HarPostData) | `postData` | The request body: `Text` or [Params](cref:Meziantou.Framework.HttpArchive.HarPostData.Params), and [MimeType](cref:Meziantou.Framework.HttpArchive.HarPostData.MimeType). |
| [HarTimings](cref:Meziantou.Framework.HttpArchive.HarTimings) | `timings` | The durations of the request phases, in milliseconds. |

Every object also has a `Comment` and an `ExtensionData` dictionary that holds the fields the format does not define.

## Namespace

Every type is in the [Meziantou.Framework.HttpArchive](cref:Meziantou.Framework.HttpArchive) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and read your first archive.
