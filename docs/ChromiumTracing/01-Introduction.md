---
title: Introduction
description: Write trace files in the Chromium trace event format, to see what a program did on a timeline in a trace viewer.
---

`Meziantou.Framework.ChromiumTracing` writes trace files in the
[Chromium trace event format](https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview):
a JSON array of events, each with a name, a timestamp, a process and a thread. Trace viewers, such as the one built
into Chromium-based browsers, show these events on a timeline, one row per thread, with the durations as boxes, the
counters as charts and the instants as marks.

Use it to see where the time goes in a build, a batch job, a test run, or any program that does work in steps: write an
event when a step starts or ends, then open the file in a viewer.

## What you can do with it

- **Write a trace file** and open it in a viewer. See [Getting started](02-GettingStarted.md).
- **Write the events of the format**: durations, complete events, instants, counters, asynchronous operations, flows,
  object lifecycles and metadata. See [Event types](03-EventTypes.md).
- **Attach data to the events**: categories, colors, stack traces and arguments of your own types. See
  [Event properties](04-EventProperties.md).
- **Write to a file or a stream**, compressed or not, from several threads. See
  [Files and streams](05-FilesAndStreams.md).

The writer only writes traces; it does not read them.

## Namespace

Every type is in the [Meziantou.Framework.ChromiumTracing](cref:Meziantou.Framework.ChromiumTracing) namespace. The package is compatible with trimming and native
AOT: events are serialized with source-generated JSON metadata.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, write your first trace and view it.
- [Event types](03-EventTypes.md): choose the event that shows what you want on the timeline.
