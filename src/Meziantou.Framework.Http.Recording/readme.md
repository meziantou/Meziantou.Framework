# Meziantou.Framework.Http.Recording

`Meziantou.Framework.Http.Recording` provides a `DelegatingHandler` to record and replay `HttpClient` traffic in tests.

## Features

- Record and replay HTTP requests/responses with `HttpRecordingHandler`
- Configurable modes: `Record`, `Replay`, `Auto`
- Pluggable request matching (`IHttpRequestMatcher`)
- Pluggable entry sanitization (`IHttpRecordingSanitizer`)
- Built-in stores:
  - `JsonHttpRecordingStore` (JSON file)
  - `HarHttpRecordingStore` (HAR 1.2 file)

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Recording
```

## Basic usage (auto record + replay)

```c#
using Meziantou.Framework.Http.Recording;

var store = new HarHttpRecordingStore("http-recordings.har");
var options = new HttpRecordingOptions
{
    Mode = HttpRecordingMode.Auto,
    MissBehavior = HttpRecordingMissBehavior.Throw,
    Sanitizer = new HeaderRemovalSanitizer("Authorization", "Cookie"),
};

using var innerHandler = new SocketsHttpHandler();
using var recordingHandler = new HttpRecordingHandler(innerHandler, store, options);
using var httpClient = new HttpClient(recordingHandler);

// First run calls the real endpoint and records the response.
var response1 = await httpClient.GetAsync("https://api.example.com/data");

// Later calls are replayed when a matching recording exists.
var response2 = await httpClient.GetAsync("https://api.example.com/data");

// Persist all recordings at the end of the test/session.
await recordingHandler.SaveAsync();
```

## Replay-only mode

```c#
using Meziantou.Framework.Http.Recording;

var store = new JsonHttpRecordingStore("http-recordings.json");
var options = new HttpRecordingOptions
{
    Mode = HttpRecordingMode.Replay,
    MissBehavior = HttpRecordingMissBehavior.Throw,
};

using var recordingHandler = new HttpRecordingHandler(store, options);
using var httpClient = new HttpClient(recordingHandler);

var response = await httpClient.GetAsync("https://api.example.com/data");
```

## Miss behavior

When no recorded response matches in replay mode:

- `Throw`: throws `HttpRecordingMissException`
- `ReturnDefault`: returns HTTP 500 with a diagnostic message
- `Passthrough`: valid in `Auto` mode, not in `Replay` mode

