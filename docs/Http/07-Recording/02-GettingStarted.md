---
title: Getting started
description: Install the package, record the responses of a service on the first run and replay them on the next ones.
---

## Install the package

```bash
dotnet add package Meziantou.Framework.Http.Recording
```

The examples omit the `using` directives: they need `using Meziantou.Framework.Http.Recording;`.

## Record, then replay

Put [HttpRecordingHandler](cref:Meziantou.Framework.Http.Recording.HttpRecordingHandler) between the `HttpClient` and the handler that sends the requests. It needs a store, the
place where the recordings are saved, and options:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_RecordThenReplay;format=md-fenced-code-block;dedent=true -->
```csharp
var store = new JsonHttpRecordingStore(recordingPath);
var options = new HttpRecordingOptions { AutoSave = true }; // Mode is Auto by default

using var innerHandler = new SocketsHttpHandler();
await using var recordingHandler = new HttpRecordingHandler(innerHandler, store, options);
using var httpClient = new HttpClient(recordingHandler);

// The first run calls the server and records the response. Later runs replay it.
var text = await httpClient.GetStringAsync(new Uri(serverUrl, "/api/text"));
Assert.Equal("hello integration", text);
```
<!-- endref -->

`serverUrl` is the address of the service the test calls. The same code behaves differently from one run to the next:

1. On the first run, the file at `recordingPath` does not exist. The request is sent to the service, and the handler
   records the request and its response.
2. When the handler is disposed, [AutoSave](cref:Meziantou.Framework.Http.Recording.HttpRecordingOptions.AutoSave) writes the recordings to the file.
3. On the next runs, the handler loads the file, finds the recorded request, and returns the recorded response. The
   service is not called, and the test passes even when it is down.

The default mode, [Auto](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Auto), is what makes this work: it replays what was recorded and records what was not. Commit the
recording file with your tests, and delete it to record again. [Recording modes](03-RecordingModes.md) describes the
other modes.

## Save the recordings

Nothing is written until the recordings are saved. Either:

- set `AutoSave = true` and dispose the handler with `await using`, as above. A synchronous `Dispose` cannot save, and
  the save still happens when the `HttpClient` disposed the handler first;
- or call `recordingHandler.SaveAsync()` yourself, for instance at the end of a test or of a test session. It waits
  for the requests that are still being recorded, so they are part of the file.

## What a recording holds

A recording, an [HttpRecordingEntry](cref:Meziantou.Framework.Http.Recording.HttpRecordingEntry), holds the method, the URI, the headers and the body of the request, and the
status code, the reason phrase, the HTTP version, the headers and the body of the response. The handler buffers the
request body before sending it, so a body that can only be read once is still recorded.

A replayed response is a new `HttpResponseMessage` built from the recording: its `RequestMessage` is the request being
replayed, and its `Content-Length` is computed from the recorded body, not copied from the recorded headers.

The handler loads the store on the first request. Call [InitializeAsync](cref:Meziantou.Framework.Http.Recording.HttpRecordingHandler.InitializeAsync) to load it earlier, for instance to see an
invalid recording file fail in a test fixture rather than in the first test.

## Next steps

- [Recording modes](03-RecordingModes.md): [Record](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Record), [Replay](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Replay) and [Auto](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Auto), and replaying without an inner handler.
- [Sanitizing secrets](06-SanitizingSecrets.md): remove credentials before the recordings reach your repository.
