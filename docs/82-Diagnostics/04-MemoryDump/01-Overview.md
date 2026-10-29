---
title: Overview
description: Write a memory dump of the current process when it detects a problem, handle failures, and open the dump.
---

`Meziantou.Framework.Diagnostics.MemoryDump` writes a memory dump of the current process on Windows, Linux and macOS,
from the process itself. Nothing has to be installed on the machine: no `dotnet-dump`, no debugger.

<!-- ref:../../../samples/Meziantou.Framework.Diagnostics.MemoryDump.Samples/ReadmeExamples.cs;region=MemoryDump_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Full dump (default)
MemoryDump.Write("app.dmp");

// Smaller dump containing the managed heap
await MemoryDump.WriteAsync("app.dmp", MemoryDumpType.WithHeap, cancellationToken);
```
<!-- endref -->

## When to write a dump

A dump is most useful when the process detects a problem it cannot explain, at the moment it happens: thread pool
starvation, a request that never completes, memory that keeps growing. The dump shows every thread and, depending on
its [type](02-DumpTypes.md), the managed objects at that moment:

<!-- ref:../../../samples/Meziantou.Framework.Diagnostics.MemoryDump.Samples/ReadmeExamples.cs;region=MemoryDump_DumpOnProblem;format=md-fenced-code-block;dedent=true -->
```csharp
if (queueDelay > TimeSpan.FromSeconds(5))
{
    MemoryDump.Write(Path.Combine(dumpDirectory, $"starvation-{DateTime.UtcNow:yyyyMMddHHmmss}.dmp"));
}
```
<!-- endref -->

Dumps are large and writing one suspends the process. Write one when a problem is detected, not periodically, and
limit how many the application can write, for instance one per problem and per hour.

## Write a dump

- `MemoryDump.Write(filePath, dumpType)` and `MemoryDump.WriteAsync(filePath, dumpType, cancellationToken)` write the
  dump to `filePath`. The default type is `MemoryDumpType.Full`.
- A relative path is resolved against the current directory. The file is overwritten if it exists. The path is used as
  is: `%` characters are not expanded as file name templates.
- Calls are serialized: when several threads request a dump at the same time, the dumps are written one after the
  other.
- On Linux and macOS, `WriteAsync` waits for the dump asynchronously, and cancelling the token stops the dump and
  deletes the incomplete file. On Windows, the dump is written synchronously: the token only cancels the wait for
  another dump to finish.

## Handle failures

Writing a dump can fail: the operating system can refuse it, the disk can be full, the runtime tool that writes the
dump can be missing. A dump is a diagnostic, so do not let its failure become a second problem:

<!-- ref:../../../samples/Meziantou.Framework.Diagnostics.MemoryDump.Samples/MemoryDumpExamples.cs;region=MemoryDump_TryWrite;format=md-fenced-code-block;dedent=true -->
```csharp
// MemoryDump supports Windows 8.1 and later, Linux, and macOS
if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1) && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
    return null;

try
{
    await MemoryDump.WriteAsync(path, MemoryDumpType.WithHeap, cancellationToken);
    return path;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // createdump not found or not allowed to attach, file not writable...
    // The exception message says why. The incomplete dump file, if any, is deleted
    Console.Error.WriteLine($"Cannot write the memory dump: {ex.Message}");
    return null;
}
```
<!-- endref -->

- `Write` and `WriteAsync` are marked as supported on Windows 8.1 and later, Linux and macOS. The platform compatibility
  analyzer (CA1416) reports the calls that are not guarded by a check such as the one of the example. On other
  platforms, they throw `PlatformNotSupportedException`.
- On Linux and macOS, a failure of the dump throws an `InvalidOperationException` whose message contains the output of
  `createdump`, and a missing `createdump` throws a `FileNotFoundException`. On Windows, a failure of the dump throws a
  `Win32Exception`, and a file that cannot be created throws the usual I/O exceptions.
- An invalid `MemoryDumpType` value throws an `ArgumentOutOfRangeException`.

The most common reason for a refused dump is a restriction of the operating system: see
[Platform requirements](03-PlatformRequirements.md).

## How it works

- **Windows**: the package captures a snapshot of the process with `PssCaptureSnapshot`, then writes it with
  `MiniDumpWriteDump` from `dbghelp.dll`, as Windows Error Reporting does. A process cannot suspend itself, so dumping
  the live process directly can fail when its memory changes while it is read.
- **Linux and macOS**: the package runs the `createdump` tool shipped with the .NET runtime. It looks for it in the
  runtime directory, then in the application directory, where self-contained and single-file applications have it.

## Open the dump

Open the dump with Visual Studio, WinDbg, `dotnet-dump analyze`, or `lldb` with the SOS extension. They show the
threads and their stacks, and the managed heap of the dumps that contain it.

A dump can contain anything the process had in memory: credentials, tokens, connection strings, personal data. Store
it and share it like the data of your application. When you send a dump to someone else, consider a
[triage dump](02-DumpTypes.md#share-a-dump).
