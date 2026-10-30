---
title: Overview
description: Record the requests an HttpClient sends and the responses it receives, and replay them in later test runs.
---

`Meziantou.Framework.Http.Recording` provides `HttpRecordingHandler`, a `DelegatingHandler` that records the requests
an `HttpClient` sends and the responses it receives, and replays them later. A test that calls a remote service runs
against the real service once, saves what it saw to a file, and replays the file on the next runs: no network access,
no flaky dependency, and the same responses every time.

## What you can do with it

- **Record and replay** with one handler: the first run records, later runs replay. See
  [Getting started](02-GettingStarted.md).
- **Choose when to call the real service**: always (`Record`), never (`Replay`), or only for what is not recorded yet
  (`Auto`). See [Recording modes](03-RecordingModes.md).
- **Control how a request finds its recording**: by method, URL and body by default, or with your own rule. See
  [Matching requests](04-MatchingRequests.md).
- **Decide what happens when nothing matches**: throw, return an error response, or call the real service. See
  [When no recording matches](05-MissBehavior.md).
- **Keep secrets out of the files** you commit: remove headers, mask query string parameters, rewrite bodies. See
  [Sanitizing secrets](06-SanitizingSecrets.md).
- **Store the recordings** as JSON, as an HTTP Archive (HAR) file, or anywhere else. See
  [Recording stores](07-RecordingStores.md).

## Namespace

Every type is in the [Meziantou.Framework.Http.Recording](cref:Meziantou.Framework.Http.Recording) namespace.
