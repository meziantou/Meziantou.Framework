---
title: Matching requests
description: How a request finds its recording, how to ignore the body, and how to write your own matching rule.
---

When a request arrives, the handler computes its fingerprint, a string that identifies the request, and looks for a
recording with the same fingerprint. Two requests match when their fingerprints are equal.

## The default fingerprint

[DefaultHttpRequestMatcher](cref:Meziantou.Framework.Http.Recording.DefaultHttpRequestMatcher) builds the fingerprint from:

- the method, compared case-insensitively;
- the scheme and the host, compared case-insensitively, and the port when it is not the default one;
- the path;
- the query string parameters, sorted, so `?a=1&b=2` and `?b=2&a=1` match;
- a SHA-256 hash of the request body, when there is one.

The headers are not part of it, and neither are credentials in the URI (`https://user:password@host/`): they are
removed when the request is captured.

Because the body is part of the fingerprint, two `POST` requests to the same URL with different payloads have their own
recordings. This matters for GraphQL, JSON-RPC, SOAP and batch endpoints.

## Ignore the request body

When the body changes between runs in a way that does not matter (a nonce, a timestamp), match on the method and the
URL only:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecordingOptions_IgnoringRequestBody;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions
{
    RequestMatcher = DefaultHttpRequestMatcher.IgnoringRequestBody,
};
```
<!-- endref -->

`new DefaultHttpRequestMatcher(matchRequestBody: false)` is the same rule.

## Write your own matcher

Implement [IHttpRequestMatcher](cref:Meziantou.Framework.Http.Recording.IHttpRequestMatcher) for any other rule. [ComputeFingerprint](cref:Meziantou.Framework.Http.Recording.IHttpRequestMatcher.ComputeFingerprint) receives an [HttpRecordingEntry](cref:Meziantou.Framework.Http.Recording.HttpRecordingEntry), which carries
the method, the URI, the headers and the body of the request, both when a recording is loaded and when a request is
looked up. This matcher ignores the query string and the body:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_PathOnlyRequestMatcher;format=md-fenced-code-block;dedent=true -->
```csharp
// Matches the requests on their method and path, whatever their query string and body
public sealed class PathOnlyRequestMatcher : IHttpRequestMatcher
{
    public string ComputeFingerprint(HttpRecordingEntry entry)
    {
        var uri = new Uri(entry.RequestUri);
        return entry.Method + " " + uri.GetLeftPart(UriPartial.Path);
    }
}
```
<!-- endref -->

Set it in [RequestMatcher](cref:Meziantou.Framework.Http.Recording.HttpRecordingOptions.RequestMatcher), both when recording and when replaying:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_UseCustomRequestMatcher;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions
{
    Mode = HttpRecordingMode.Replay,
    RequestMatcher = new PathOnlyRequestMatcher(),
};
using var recordingHandler = new HttpRecordingHandler(new JsonHttpRecordingStore(recordingPath), options);
using var httpClient = new HttpClient(recordingHandler);

// Recorded as /api/items/42?timestamp=1: the query string is not part of the fingerprint
var json = await httpClient.GetStringAsync(new Uri(serverUrl, "/api/items/42?timestamp=2"));
Assert.Equal("""{"id":42,"name":"item-42"}""", json);
```
<!-- endref -->

A fingerprint must be deterministic: the same request must give the same string on every run and on every machine.

The fingerprint of a request is computed after the [sanitizers](06-SanitizingSecrets.md) ran on it, so a matcher sees
redacted values, the same ones that were saved.

## Recordings are consumed in order

Each recording answers one request. A request sent three times needs three recordings, replayed in the order they
were recorded; the fourth one is a miss:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_RecordingsAreConsumedInOrder;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };
using var recordingHandler = new HttpRecordingHandler(new JsonHttpRecordingStore(recordingPath), options);
using var httpClient = new HttpClient(recordingHandler);

// The recording holds one GET /api/text: it answers the first request only
Assert.Equal("hello integration", await httpClient.GetStringAsync(new Uri(serverUrl, "/api/text")));
await Assert.ThrowsAsync<HttpRecordingMissException>(() => httpClient.GetStringAsync(new Uri(serverUrl, "/api/text")));
```
<!-- endref -->

This is what lets you record an endpoint that answers differently on each call, such as a job status that goes from
"running" to "done".
