---
title: Overview
description: What a context snapshot is, when to capture one, and what it costs.
---

`Meziantou.Framework.Diagnostics.ContextSnapshot` captures the execution context of the current process: the machine,
the operating system, the .NET runtime, the process, and their configuration. You get one object, serializable to
JSON, that answers the questions you would otherwise ask the person who reports a problem.

<!-- ref:../../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=ContextSnapshotBuilder_AddDefault;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = new ContextSnapshotBuilder();
builder.AddDefault();
var snapshot = builder.BuildSnapshot();

// Serialize for logging
var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Strict) { WriteIndented = true });
Assert.StartsWith("{", json);
```
<!-- endref -->

## When to capture a snapshot

- **At startup**, written once to the logs, so that every log file says where it comes from.
- **When an error occurs** that you cannot reproduce, attached to the error report.
- **In a "diagnostics" command or endpoint** of your application, that a user runs and sends you the output.

## What it costs

A snapshot is not free: [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault) enumerates the loaded assemblies, the modules of the process, the drives and the
environment variables. Some information comes from the operating system: on macOS, the CPU information comes from the
`sysctl -a` command, and on Windows, some values come from WMI and the Windows Security Center. Capture a snapshot
once, or when something goes wrong, not on every request. The CPU information is read once per process and reused.

Each `Add*` method reads the values when you call it: the snapshot shows the state of the process at that moment,
not when you call [BuildSnapshot](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.BuildSnapshot).

## Pages

- [Capturing a snapshot](02-CapturingASnapshot.md): pick the information you need, add your own values, and serialize
  the result.
- [What a snapshot contains](03-WhatASnapshotContains.md): the keys of the snapshot and the type of each value.
- [Sensitive data](04-SensitiveData.md): the snapshot holds paths, user names and environment variables; keep the
  secrets out of it.
