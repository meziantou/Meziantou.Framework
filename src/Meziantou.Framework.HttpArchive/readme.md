# Meziantou.Framework.HttpArchive

`Meziantou.Framework.HttpArchive` provides a .NET model for the HAR 1.2 (HTTP Archive) format, with parsing, serialization, and helpers to convert HAR entries to `HttpRequestMessage` / `HttpResponseMessage`.

> [!WARNING]
> **HAR files contain credentials.** An archive records `Authorization` headers, `Cookie` and `Set-Cookie`
> values, bearer tokens, API keys and complete request bodies exactly as they were sent. Treat a `.har` as a
> secret: do not attach one to a bug report, support ticket or issue, and do not commit one, without removing
> the sensitive entries first. This library reads and writes what the file contains and does not redact
> anything for you.

## Installation

```bash
dotnet add package Meziantou.Framework.HttpArchive
```

## Nullability

Producers do not always honor the HAR schema: an archive can hold an explicit `null` for any field. The model is
nullable throughout so those archives still parse, and it is up to the caller to decide what an absent value means.
A property left out of the JSON keeps its default (`""` for strings, an empty list for collections); only an explicit
`null` yields `null`.

## Parse a HAR file

The types are in the `Meziantou.Framework.HttpArchive` namespace.

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

## Create and write a HAR document

`WriteToAsync` writes to any stream, such as a `MemoryStream` or the `FileStream` returned by `File.Create("output.har")`:

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

## Convert HAR entries to HttpClient messages

<!-- ref:../../samples/Meziantou.Framework.Http.Samples/HttpArchiveExamples.cs;region=HarEntry_ConvertToHttpMessages;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HarDocument.Parse(File.ReadAllText("traffic.har"));
var entry = document.Log!.Entries![0];

// Throws InvalidOperationException when the archived entry has no request/response,
// or when the request declares no method or URL.
using var request = entry.ToHttpRequestMessage();
using var response = entry.ToHttpResponseMessage();
```
<!-- endref -->

