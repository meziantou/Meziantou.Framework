---
title: Missing and null values
description: Why every property of the model is nullable, and how a missing field differs from an explicit null.
---

The HAR format defines which fields are required, but the tools that produce archives do not always follow it: an
archive can hold an explicit `null` for any field. So that those archives still parse, every reference property of
the model is nullable, and it is up to you to decide what an absent value means.

A field that is missing from the JSON and a field that is `null` are read differently:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarDocumentTests.cs;region=HarDocument_Nullability;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HarDocument.Parse("""
    {
      "log": {
        "entries": [
          { "request": { "method": "GET", "url": "https://example.com/", "headers": null } },
          { "request": null }
        ]
      }
    }
    """);

var entries = document.Log!.Entries!;

// A missing property keeps its default value
Assert.Equal("", entries[0].Request!.HttpVersion);
Assert.Empty(entries[0].Request!.Cookies!);

// An explicit null is kept as null
Assert.Null(entries[0].Request!.Headers);
Assert.Null(entries[1].Request);
```
<!-- endref -->

- A missing field keeps the default value of the property: `""` for most strings, an empty list for the collections,
  an empty object for `HarEntry.Request`, `Response`, `Cache` and `Timings`, `1.2` for `HarLog.Version`.
- An explicit `null` gives `null`, even for a collection or an object.
- The optional fields of the format, such as `Comment`, `PostData` or `HarLog.Pages`, are `null` when they are
  missing.

When you read an archive you did not create, check for `null` with `?.` and `??`, as the examples of this
documentation do. When you write an archive, a `null` property is left out of the JSON.

`ToHttpRequestMessage` and `ToHttpResponseMessage` accept these values: `null` headers, cookies or MIME types give a
message without them. They throw an `InvalidOperationException` when the entry has no request or no response, or
when the method or the URL of the request is `null`. A request also needs a method and an absolute URL: an empty
method, or a URL that `Uri` cannot parse, throws like `new HttpMethod` and `new Uri` do. See
[Converting entries](05-ConvertingEntries.md).
