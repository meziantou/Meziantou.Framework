---
title: When no recording matches
description: Throw, return an error response or call the real service when a request has no recording.
---

`HttpRecordingOptions.MissBehavior` decides what happens to a request that matches no recording:

| Value | Behavior |
| --- | --- |
| `Throw` | Throws an `HttpRecordingMissException`. |
| `ReturnDefault` | Returns a `500 Internal Server Error` response whose body says which request had no recording. |
| `Passthrough` | Sends the request to the inner handler. In `Auto` mode, the response is also recorded; in `Replay` mode, it is not. |

When it is not set, the default depends on the mode: `Passthrough` in `Auto` mode, so that the missing recording is
created, and `Throw` in `Replay` mode. The option is not used in `Record` mode, where every request is sent.

## Fail on any request that is not recorded

Setting the option also applies to `Auto` mode. `Auto` with `Throw` replays what was recorded and fails on anything
else, without ever sending a request:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecordingOptions_AutoWithThrowOnMiss;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions
{
    Mode = HttpRecordingMode.Auto,
    MissBehavior = HttpRecordingMissBehavior.Throw,
};
```
<!-- endref -->

`HttpRecordingMissException` derives from `InvalidOperationException`. Its message is safe to log: it contains no
credentials from the URI and masks every query string value. `Method` and `RequestUri` give the request as it was
sent:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_MissException;format=md-fenced-code-block;dedent=true -->
```csharp
var exception = await Assert.ThrowsAsync<HttpRecordingMissException>(
    () => httpClient.GetAsync(new Uri("https://example.com/api/items?token=secret")));

// The message is safe to log: query values are masked
Assert.Equal("No recorded response found for GET https://example.com/api/items?token=***.", exception.Message);

// The properties hold the request as it was sent
Assert.Equal("GET", exception.Method);
Assert.Equal("https://example.com/api/items?token=secret", exception.RequestUri);
```
<!-- endref -->

## Return an error response

`ReturnDefault` lets the code under test handle the miss like a server error. The body of the response gives the
request, with the same redaction as the exception message:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_ReturnDefaultOnMiss;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions
{
    Mode = HttpRecordingMode.Replay,
    MissBehavior = HttpRecordingMissBehavior.ReturnDefault,
};
using var recordingHandler = new HttpRecordingHandler(new JsonHttpRecordingStore(recordingPath), options);
using var httpClient = new HttpClient(recordingHandler);

using var response = await httpClient.GetAsync(new Uri("https://example.com/api/items/1"));
Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
Assert.Equal("No recorded response found for GET https://example.com/api/items/1.", await response.Content.ReadAsStringAsync());
```
<!-- endref -->

## Call the real service

`Passthrough` needs an inner handler. In `Replay` mode, it sends the requests that have no recording to the service
without recording them, so the store stays as it is.
