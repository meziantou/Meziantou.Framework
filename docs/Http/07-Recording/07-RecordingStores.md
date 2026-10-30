---
title: Recording stores
description: Save the recordings as JSON or as an HTTP Archive file, or write your own store.
---

A store loads and saves the recordings. The package provides two, both backed by a file:

| Store | Format |
| --- | --- |
| [JsonHttpRecordingStore](cref:Meziantou.Framework.Http.Recording.JsonHttpRecordingStore) | A JSON array of [HttpRecordingEntry](cref:Meziantou.Framework.Http.Recording.HttpRecordingEntry). The bodies are base64. |
| [HarHttpRecordingStore](cref:Meziantou.Framework.Http.Recording.HarHttpRecordingStore) | An HTTP Archive (HAR 1.2) file. |

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_HarStore;format=md-fenced-code-block;dedent=true -->
```csharp
var store = new HarHttpRecordingStore(recordingPath);
var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

using var innerHandler = new SocketsHttpHandler();
using var recordingHandler = new HttpRecordingHandler(innerHandler, store, options);
using var httpClient = new HttpClient(recordingHandler);

await httpClient.GetStringAsync(new Uri(serverUrl, "/api/text"));

// Writes an HTTP Archive (HAR 1.2) file
await recordingHandler.SaveAsync();
```
<!-- endref -->

Choose HAR to open the recordings in the tools that read HTTP Archive files, or to replay a HAR file saved by another
tool. To read or write HAR files from code, see [HttpArchive](../../HttpArchive/01-Introduction.md).

Both stores behave the same way:

- A file that does not exist is an empty store, so the first run starts from nothing.
- Saving creates the directory when needed, and is atomic: the file is written next to the destination, then moved into
  place, so an interrupted save cannot destroy an existing recording.
- A file that cannot be read (invalid JSON, a HAR file without `log.entries`, an entry without a method) throws an
  `InvalidDataException` that names the file. Delete or repair it.

## Write your own store

Implement [IHttpRecordingStore](cref:Meziantou.Framework.Http.Recording.IHttpRecordingStore) to keep the recordings elsewhere, for instance in embedded resources or in a database:

- [LoadAsync](cref:Meziantou.Framework.Http.Recording.IHttpRecordingStore.LoadAsync) returns every recording, or an empty list when there is none yet.
- [SaveAsync](cref:Meziantou.Framework.Http.Recording.IHttpRecordingStore.SaveAsync) receives every recording to keep and replaces what the store held.

The handler calls [LoadAsync](cref:Meziantou.Framework.Http.Recording.IHttpRecordingStore.LoadAsync) on the first request or from [InitializeAsync](cref:Meziantou.Framework.Http.Recording.HttpRecordingHandler.InitializeAsync), except in [Record](cref:Meziantou.Framework.Http.Recording.HttpRecordingMode.Record) mode, and the store's
[SaveAsync](cref:Meziantou.Framework.Http.Recording.IHttpRecordingStore.SaveAsync) from its own [SaveAsync](cref:Meziantou.Framework.Http.Recording.IHttpRecordingStore.SaveAsync) or when it is disposed with [AutoSave](cref:Meziantou.Framework.Http.Recording.HttpRecordingOptions.AutoSave).
