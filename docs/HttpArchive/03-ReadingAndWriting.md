---
title: Reading and writing
description: Parse a HAR document from a string or a stream, create one, and write it as JSON.
---

## Read an archive

[HarDocument](cref:Meziantou.Framework.HttpArchive.HarDocument) reads JSON from three sources:

| Method | Source |
| --- | --- |
| `Parse(string)` | A JSON string. |
| `Parse(Stream)` | A UTF-8 stream. |
| `ParseAsync(Stream, CancellationToken)` | A UTF-8 stream, asynchronously. |

Property names are matched case-insensitively. Invalid JSON throws a `JsonException`, and a document that is the JSON
`null` throws an `InvalidOperationException`. A stream is not closed.

## Create and write an archive

Build the model with object initializers, then write it with [WriteTo](cref:Meziantou.Framework.HttpArchive.HarDocument.WriteTo) or [WriteToAsync](cref:Meziantou.Framework.HttpArchive.HarDocument.WriteToAsync), to any stream:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarDocumentTests.cs;region=HarDocument_CreateAndWrite;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new HarDocument
{
    Log = new HarLog
    {
        Version = "1.2",
        Creator = new HarCreator
        {
            Name = "MyTool",
            Version = "1.0.0",
        },
        Entries =
        [
            new HarEntry
            {
                StartedDateTime = DateTimeOffset.UtcNow,
                Request = new HarRequest
                {
                    Method = "GET",
                    Url = "https://example.com/api/data",
                },
                Response = new HarResponse
                {
                    Status = 200,
                    StatusText = "OK",
                },
            },
        ],
    },
};

using var stream = new MemoryStream();
await document.WriteToAsync(stream, indented: true);
```
<!-- endref -->

To write to a file, pass the `FileStream` returned by `File.Create("output.har")`. [ToJsonString](cref:Meziantou.Framework.HttpArchive.HarDocument.ToJsonString) returns the JSON as a
string:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarDocumentTests.cs;region=HarDocument_ToJsonString;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new HarDocument();
document.Log!.Creator = new HarCreator { Name = "MyTool", Version = "1.0" };
document.Log.Entries!.Add(new HarEntry
{
    StartedDateTime = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
    Request = new HarRequest { Method = "GET", Url = "https://example.com/" },
    Response = new HarResponse { Status = 204, StatusText = "No Content" },
});

var json = document.ToJsonString(indented: true);

var copy = HarDocument.Parse(json);
Assert.Equal("1.2", copy.Log!.Version);
Assert.Equal(204, Assert.Single(copy.Log.Entries!).Response!.Status);
```
<!-- endref -->

- `indented: true` formats the JSON on several lines. The default is compact JSON.
- A new [HarDocument](cref:Meziantou.Framework.HttpArchive.HarDocument) already has a [Log](cref:Meziantou.Framework.HttpArchive.HarDocument.Log) with [Version](cref:Meziantou.Framework.HttpArchive.HarLog.Version) `1.2`, a [Creator](cref:Meziantou.Framework.HttpArchive.HarLog.Creator) and an empty [Entries](cref:Meziantou.Framework.HttpArchive.HarLog.Entries) list, so you can
  add entries to it.
- A new [HarEntry](cref:Meziantou.Framework.HttpArchive.HarEntry) has empty [Request](cref:Meziantou.Framework.HttpArchive.HarEntry.Request), [Response](cref:Meziantou.Framework.HttpArchive.HarEntry.Response), [Cache](cref:Meziantou.Framework.HttpArchive.HarEntry.Cache) and [Timings](cref:Meziantou.Framework.HttpArchive.HarEntry.Timings) objects; set the values you know.
- A property whose value is `null` is not written.

## Custom fields

Tools add their own fields to the objects of the format, with names that start with `_`. The fields the model does
not define are kept in the `ExtensionData` dictionary of each object, and written back:

<!-- ref:../../tests/Meziantou.Framework.HttpArchive.Tests/HarDocumentTests.cs;region=HarDocument_ExtensionData;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HarDocument.Parse("""
    {
      "log": {
        "version": "1.2",
        "entries": [
          { "_resourceType": "xhr", "request": { "method": "GET", "url": "https://example.com/" } }
        ]
      }
    }
    """);

var entry = Assert.Single(document.Log!.Entries!);
Assert.Equal("xhr", entry.ExtensionData!["_resourceType"].GetString());

// Custom fields are written back
Assert.Contains("\"_resourceType\":\"xhr\"", document.ToJsonString());
```
<!-- endref -->

The values are `JsonElement`s. To add a field, add an entry to the dictionary; it is `null` until the object has a
field to hold, so create it first.

Values the format uses for unknown data, such as `-1` for a timing that does not apply, are read and written as they
are.
