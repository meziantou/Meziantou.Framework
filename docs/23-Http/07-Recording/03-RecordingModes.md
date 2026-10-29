---
title: Recording modes
description: Always call the service, never call it, or call it only for the requests that are not recorded yet.
---

`HttpRecordingOptions.Mode` decides when the real service is called:

| Mode | Loads the store | Calls the service | Saving |
| --- | --- | --- | --- |
| `Auto` (default) | Yes | Only for a request that has no recording, by default | Writes the loaded recordings and the new ones |
| `Record` | No | For every request | Replaces the content of the store with the new recordings |
| `Replay` | Yes | Never, by default | Adds nothing: there is no new recording |

What happens to a request that has no recording depends on `MissBehavior`; the defaults above are `Passthrough` in
`Auto` mode and `Throw` in `Replay` mode. See [When no recording matches](05-MissBehavior.md).

## Auto: record what is missing

`Auto` replays the recordings it loaded and records the requests it cannot answer, so the store grows as tests send new
requests. It is the mode to use while writing tests. See [Getting started](02-GettingStarted.md).

A response recorded during a session is not replayed in the same session. Two identical requests sent while recording
are both sent to the service and both recorded, so the file can replay the scenario as it ran.

## Record: refresh the recordings

`Record` sends every request to the service and does not load the existing recordings. Saving replaces the content of
the store with the recordings of the session. Use it to refresh the recordings after the service changed.

## Replay: never call the service

`Replay` answers every request from the recordings. Use it on CI, where a test must not depend on the service. Without
an inner handler, nothing can reach the network:

<!-- ref:../../../tests/Meziantou.Framework.Http.Recording.Tests/HttpRecordingHandlerTests.cs;region=HttpRecording_ReplayWithoutInnerHandler;format=md-fenced-code-block;dedent=true -->
```csharp
var store = new JsonHttpRecordingStore(recordingPath);
var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

// Without an inner handler, no request can reach the network
using var recordingHandler = new HttpRecordingHandler(store, options);
using var httpClient = new HttpClient(recordingHandler);

Assert.Equal("hello integration", await httpClient.GetStringAsync(new Uri(serverUrl, "/api/text")));

// Nothing was recorded for this request: Replay throws by default
await Assert.ThrowsAsync<HttpRecordingMissException>(() => httpClient.GetStringAsync(new Uri(serverUrl, "/api/items/1")));
```
<!-- endref -->

The constructor without an inner handler accepts only the `Replay` mode, with a `MissBehavior` other than
`Passthrough`: there is nothing to forward a request to. Any other combination throws an `ArgumentException`.

## Switching modes between environments

The mode is an ordinary property, so a test suite can choose it from its environment: `Auto` on a developer machine,
`Replay` on CI to fail on a missing recording, `Record` when the recordings must be refreshed. Another way to fail
on a missing recording without changing the mode is `Auto` with `MissBehavior = Throw`. See
[When no recording matches](05-MissBehavior.md).
