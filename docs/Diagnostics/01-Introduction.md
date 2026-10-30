---
title: Introduction
description: Two packages to investigate a problem in production, a snapshot of the execution context and a memory dump of the current process.
---

When an application misbehaves on a machine you cannot access, the first questions are always the same: which
operating system, which runtime, which culture, which environment variables, how much memory? And when the problem is
a hang or a leak, you want to look inside the process at the moment it happened. The `Meziantou.Framework.Diagnostics.*`
packages answer both from inside the application, without installing anything on the machine.

## What you can do with them

- **Capture the execution context** of the process in one object: operating system, CPU, drives, culture, time zone,
  process, garbage collector, thread pool, environment variables, `AppContext` switches and more. Serialize it to JSON
  and attach it to a bug report or a log. See [Context snapshot](03-ContextSnapshot/01-Overview.md).
- **Redact secrets** from the environment variables of the snapshot. See
  [Sensitive data](03-ContextSnapshot/04-SensitiveData.md).
- **Write a memory dump** of the current process on Windows, Linux and macOS, when the application detects a problem
  it cannot explain, and open it in Visual Studio, WinDbg or `dotnet-dump`. See
  [Memory dumps](04-MemoryDump/01-Overview.md).
- **Know the platform requirements** of a dump: ptrace on Linux and in containers, the `get-task-allow` entitlement on
  macOS. See [Platform requirements](04-MemoryDump/03-PlatformRequirements.md).

## Packages

| Package | Main types | Pages |
| --- | --- | --- |
| `Meziantou.Framework.Diagnostics.ContextSnapshot` | `ContextSnapshotBuilder` | [Context snapshot](03-ContextSnapshot/01-Overview.md) |
| `Meziantou.Framework.Diagnostics.MemoryDump` | `MemoryDump`, `MemoryDumpType` | [Memory dumps](04-MemoryDump/01-Overview.md) |

The packages are independent: install only the one you need.

## Namespaces

- `ContextSnapshotBuilder` and the snapshot types are in the [Meziantou.Framework.Diagnostics.ContextSnapshot](cref:Meziantou.Framework.Diagnostics.ContextSnapshot)
  namespace.
- `MemoryDump` and `MemoryDumpType` are in the [Meziantou.Framework.Diagnostics](cref:Meziantou.Framework.Diagnostics.MemoryDump!N:Meziantou.Framework.Diagnostics) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install a package, capture a snapshot and write a dump.
- [Logging](../Logging/01-Introduction.md): the logging packages, to write a snapshot to the logs at startup.
