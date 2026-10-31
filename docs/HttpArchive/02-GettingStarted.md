---
title: Getting started
description: Install the package, read a HAR file and walk its entries.
---

## Install the package

Add the `Meziantou.Framework.HttpArchive` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.HttpArchive" />

The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.HttpArchive;`.

## Read a HAR file

[HarDocument.ParseAsync](cref:Meziantou.Framework.HttpArchive.HarDocument.ParseAsync) reads an archive from a stream, such as a file exported from a browser:

<!-- ref:../../samples/Meziantou.Framework.Http.Samples/HttpArchiveExamples.cs;region=HarDocument_ParseFile;format=md-fenced-code-block;dedent=true -->
```csharp
await using var stream = File.OpenRead("traffic.har");
var document = await HarDocument.ParseAsync(stream);

foreach (var entry in document.Log?.Entries ?? [])
{
    Console.WriteLine($"{entry.Request?.Method} {entry.Request?.Url} -> {entry.Response?.Status}");
}
```
<!-- endref -->

`document.Log.Entries` lists the requests in the order the archive stores them. The `?.` and `?? []` are there
because the model is nullable: an archive can hold `null` for any field. See
[Missing and null values](04-MissingAndNullValues.md).

## Read the entries

[HarDocument.Parse](cref:Meziantou.Framework.HttpArchive.HarDocument.Parse) reads an archive from a string. Each entry has the [Request](cref:Meziantou.Framework.HttpArchive.HarEntry.Request), the [Response](cref:Meziantou.Framework.HttpArchive.HarEntry.Response), and the time it
took in milliseconds:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarDocumentTests.cs;region=HarDocument_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HarDocument.Parse("""
    {
      "log": {
        "version": "1.2",
        "creator": { "name": "MyTool", "version": "1.0" },
        "entries": [
          {
            "startedDateTime": "2026-01-15T10:00:00.000Z",
            "time": 42.5,
            "request": { "method": "GET", "url": "https://example.com/api/items" },
            "response": { "status": 200, "statusText": "OK", "content": { "size": 2, "mimeType": "application/json", "text": "[]" } }
          }
        ]
      }
    }
    """);

var entry = Assert.Single(document.Log!.Entries!);
Assert.Equal("GET", entry.Request!.Method);
Assert.Equal("https://example.com/api/items", entry.Request.Url);
Assert.Equal(200, entry.Response!.Status);
Assert.Equal("[]", entry.Response.Content!.Text);
Assert.Equal(42.5, entry.Time);
```
<!-- endref -->

The response body is in `Response.Content.Text`. A binary body is stored as base64: see
[Get the bytes of a body](05-ConvertingEntries.md#get-the-bytes-of-a-body).

## Next steps

- [Reading and writing](03-ReadingAndWriting.md): create an archive and write it.
- [Converting entries](05-ConvertingEntries.md): turn an entry into an `HttpRequestMessage` and an
  `HttpResponseMessage`.
- [Sharing archives](06-SharingArchives.md): remove the credentials before you share a file.
