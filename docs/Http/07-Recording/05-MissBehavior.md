---
title: When no recording matches
description: Throw, return an error response or call the real service when a request has no recording.
---

[HttpRecordingOptions.MissBehavior](cref:Meziantou.Framework.Http.Recording.HttpRecordingOptions.MissBehavior) decides what happens to a request that matches no recording:

| Value | Behavior |
| --- | --- |
| [Throw](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.Throw) | Throws an [HttpRecordingMissException](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissException). |
| [ReturnDefault](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.ReturnDefault) | Returns a `500 Internal Server Error` response whose body says which request had no recording. |
| [Passthrough](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.Passthrough) | Sends the request to the inner handler. In [Auto](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Auto) mode, the response is also recorded; in [Replay](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Replay) mode, it is not. |

When it is not set, the default depends on the mode: [Passthrough](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.Passthrough) in [Auto](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Auto) mode, so that the missing recording is
created, and [Throw](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.Throw) in [Replay](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Replay) mode. The option is not used in [Record](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Record) mode, where every request is sent.

## Fail on any request that is not recorded

Setting the option also applies to [Auto](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Auto) mode. [Auto](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Auto) with [Throw](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.Throw) replays what was recorded and fails on anything
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

[HttpRecordingMissException](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissException) derives from `InvalidOperationException`. Its message is safe to log: it contains no
credentials from the URI and masks every query string value. [Method](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissException.Method) and [RequestUri](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissException.RequestUri) give the request as it was
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

[ReturnDefault](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.ReturnDefault) lets the code under test handle the miss like a server error. The body of the response gives the
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

[Passthrough](cref:Meziantou.Framework.Http.Recording.HttpRecordingMissBehavior.Passthrough) needs an inner handler. In [Replay](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Replay) mode, it sends the requests that have no recording to the service
without recording them, so the store stays as it is.
