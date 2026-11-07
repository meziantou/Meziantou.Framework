---
title: Sanitizing secrets
description: Remove headers, mask query string parameters and rewrite bodies before the recordings are saved.
---

Recording files are usually committed, so anything secret in a request or a response ends up in the repository:
authentication headers, cookies, API keys in the URL, tokens in the bodies. Sanitizers rewrite the recordings before
they are saved:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecordingOptions_Sanitizers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions
{
    Sanitizers =
    {
        new HeaderRemovalSanitizer("Authorization", "Cookie"),
        new UriQueryParameterSanitizer("api_key", "sig"),
    },
};
```
<!-- endref -->

- [HeaderRemovalSanitizer](cref:Meziantou.Framework.Http.Recording.HeaderRemovalSanitizer) removes the named headers, compared case-insensitively, from the request and the response.
  It does not touch the URI or the bodies.
- [UriQueryParameterSanitizer](cref:Meziantou.Framework.Http.Recording.UriQueryParameterSanitizer) replaces the value of the named query string parameters with `***`.
- Credentials in the URI (`https://user:password@host/`) are always removed, without a sanitizer.

The sanitizers run in the order of the [Sanitizers](cref:Meziantou.Framework.Http.Recording.HttpRecordingOptions.Sanitizers) list. There are none by default.

## Rewrite a body

Implement [IHttpRecordingSanitizer](cref:Meziantou.Framework.Http.Recording.IHttpRecordingSanitizer) to change anything else. [Sanitize](cref:Meziantou.Framework.Http.Recording.IHttpRecordingSanitizer.Sanitize) receives the [HttpRecordingEntry](cref:Meziantou.Framework.Http.Recording.HttpRecordingEntry) and changes it
in place; [RequestBody](cref:Meziantou.Framework.Http.Recording.HttpRecordingEntry.RequestBody) and [ResponseBody](cref:Meziantou.Framework.Http.Recording.HttpRecordingEntry.ResponseBody) are the bodies as bytes. This sanitizer replaces a known secret, such as a
token read from the test configuration, in the response bodies:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_ResponseBodySanitizer;format=md-fenced-code-block;dedent=true -->
```csharp
// Replaces a known secret in the response bodies. It runs before the entries are saved.
public sealed class ResponseBodySanitizer(string secret) : IHttpRecordingSanitizer
{
    public void Sanitize(HttpRecordingEntry entry)
    {
        if (entry.ResponseBody is { } body)
        {
            var text = Encoding.UTF8.GetString(body);
            entry.ResponseBody = Encoding.UTF8.GetBytes(text.Replace(secret, "***", StringComparison.Ordinal));
        }
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_UseResponseBodySanitizer;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpRecordingOptions
{
    Sanitizers = { new ResponseBodySanitizer("s3cr3t-t0ken") },
};
```
<!-- endref -->

A sanitizer changes the recording, not the response: while recording, the code under test receives the real response;
when replaying, it receives the sanitized one. A test that replays must therefore not depend on a value a sanitizer
removed.

## Sanitizers must be deterministic

The sanitizers also run on each incoming request before its [fingerprint](04-MatchingRequests.md) is computed. A
request whose `api_key` is masked in the recording is masked the same way when it is looked up, so it still matches.
This only works when a sanitizer gives the same result for the same input: replace a value with a constant, never
with a random or time-based one.
