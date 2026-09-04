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
    Sanitizers = { new HeaderRemovalSanitizer("Authorization", "Cookie") },
};

using var innerHandler = new SocketsHttpHandler();
await using var recordingHandler = new HttpRecordingHandler(innerHandler, store, options);
using var httpClient = new HttpClient(recordingHandler);

// The first run calls the real endpoint and records the response.
var response1 = await httpClient.GetAsync("https://api.example.com/data");

// Persist all recordings at the end of the test/session.
await recordingHandler.SaveAsync();
```

On a later run, the recorded response is replayed instead of calling the real endpoint.

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

The constructor without an inner handler can only be used with `HttpRecordingMode.Replay` and a miss behavior other
than `Passthrough`: there is nothing to forward a request to. Any other combination throws an `ArgumentException`.

## Matching

A recording is matched by a fingerprint. `DefaultHttpRequestMatcher` uses the HTTP method, the URL with its query
parameters sorted, and **the request body**. Two `POST`s to the same URL carrying different payloads therefore do not
match each other, which matters for GraphQL, JSON-RPC, SOAP and batch endpoints.

If the body varies between runs in a way that should not affect matching (a nonce, a timestamp), match on the URL only:

```c#
var options = new HttpRecordingOptions
{
    RequestMatcher = DefaultHttpRequestMatcher.IgnoringRequestBody,
};
```

Implement `IHttpRequestMatcher` for anything else. The entry passed to `ComputeFingerprint` carries the method, URI,
headers and body on both the record and the replay path, so a matcher may read any of them.

**Recordings are consumed in order.** Each stored entry replays at most once, so a request issued three times needs
three recordings. This is what makes it possible to record an endpoint that returns something different on each call.

## Miss behavior

`MissBehavior` controls what happens when no recorded response matches. When it is left unset, the default depends on
the mode: `Throw` in `Replay` mode, and `Passthrough` in `Auto` mode so that a missing recording gets created.

- `Throw`: throws `HttpRecordingMissException`
- `ReturnDefault`: returns HTTP 500 with a diagnostic message
- `Passthrough`: forwards the request to the inner handler. In `Auto` mode the response is also recorded; in `Replay`
  mode it is not.

Setting it explicitly applies to `Auto` as well. `Mode = Auto` with `MissBehavior = Throw` replays what has been
recorded and fails on anything else, without ever performing a real HTTP call:

```c#
var options = new HttpRecordingOptions
{
    Mode = HttpRecordingMode.Auto,
    MissBehavior = HttpRecordingMissBehavior.Throw,
};
```

## Recording modes and the store

- `Record` does not load the existing recordings, so saving **replaces** the content of the store. Use it to refresh
  recordings against the live API.
- `Auto` loads the existing recordings and saves them together with whatever was recorded during the session, so the
  store grows as new interactions are encountered. Entries recorded during a session are not replayed within that same
  session.
- `Replay` never writes.

Nothing is written until `SaveAsync` is called. Set `AutoSave = true` and dispose the handler with `await using` to
save automatically:

```c#
var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto, AutoSave = true };
await using var recordingHandler = new HttpRecordingHandler(innerHandler, store, options);
```

Saving is atomic: the file is written next to the destination and moved into place, so an interrupted save cannot
destroy an existing recording.

## Sanitizing secrets

Recording files are usually committed, so anything secret in a request or response ends up in the repository.

```c#
var options = new HttpRecordingOptions
{
    Sanitizers =
    {
        new HeaderRemovalSanitizer("Authorization", "Cookie"),
        new UriQueryParameterSanitizer("api_key", "sig"),
    },
};
```

- `HeaderRemovalSanitizer` removes headers. It does **not** touch the URL or the bodies.
- `UriQueryParameterSanitizer` masks the value of the named query string parameters.
- Credentials in the userinfo component (`https://user:password@host/`) are removed unconditionally when a request is
  captured.
- To redact a body, implement `IHttpRecordingSanitizer` and rewrite `entry.RequestBody` / `entry.ResponseBody`.

Sanitizers run on entries before they are persisted **and** on incoming requests before matching, so redacting a value
the matcher reads (such as a query parameter) does not prevent replay. A sanitizer must therefore be deterministic.
