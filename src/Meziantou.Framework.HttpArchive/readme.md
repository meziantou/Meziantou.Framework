# Meziantou.Framework.HttpArchive

`Meziantou.Framework.HttpArchive` provides a .NET model for the HAR 1.2 (HTTP Archive) format, with parsing, serialization, and helpers to convert HAR entries to `HttpRequestMessage` / `HttpResponseMessage`.

## Installation

```bash
dotnet add package Meziantou.Framework.HttpArchive
```

## Parse a HAR file

```c#
using Meziantou.Framework.HttpArchive;

await using var stream = File.OpenRead("traffic.har");
var document = await HarDocument.ParseAsync(stream);

foreach (var entry in document.Log.Entries)
{
    Console.WriteLine($"{entry.Request.Method} {entry.Request.Url} -> {entry.Response.Status}");
}
```

## Create and write a HAR document

```c#
using Meziantou.Framework.HttpArchive;

var document = new HarDocument();
document.Log.Version = "1.2";
document.Log.Creator = new HarCreator
{
    Name = "MyTool",
    Version = "1.0.0",
};

document.Log.Entries.Add(new HarEntry
{
    StartedDateTime = DateTimeOffset.UtcNow,
    Request =
    {
        Method = "GET",
        Url = "https://example.com/api/data",
    },
    Response =
    {
        Status = 200,
        StatusText = "OK",
    },
});

await using var stream = File.Create("output.har");
await document.WriteToAsync(stream, indented: true);
```

## Convert HAR entries to HttpClient messages

```c#
using Meziantou.Framework.HttpArchive;

var document = HarDocument.Parse(File.ReadAllText("traffic.har"));
var entry = document.Log.Entries[0];

using var request = entry.ToHttpRequestMessage();
using var response = entry.ToHttpResponseMessage();
```

