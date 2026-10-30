---
title: Dump types
description: Choose between a normal, heap, triage and full dump, depending on what you need to investigate.
---

The `dumpType` parameter of `Write` and `WriteAsync` chooses what the dump contains, and therefore its size:

| `MemoryDumpType` | Content | Use it to |
| --- | --- | --- |
| `Normal` | Module and thread lists, all stacks, and exception information | See what every thread is doing: a deadlock, a hang, thread pool starvation |
| `WithHeap` | Same as `Normal`, plus handle information and all memory except mapped images | Also inspect the managed objects: a memory leak, the state of an object |
| `Triage` | Same as `Normal`, with personally identifiable information such as paths and passwords removed | Send the stacks to someone else |
| `Full` | All the memory of the process, including module images | Keep everything, when you cannot reproduce the problem. This is the default |

The types match the `DumpType` enumeration of `Microsoft.Diagnostics.NETCore.Client` and the `--normal`, `--withheap`,
`--triage` and `--full` options of `createdump`, the tool of the .NET runtime that writes the dumps on Linux and macOS.

## Size

A full dump is as large as the memory used by the process, often hundreds of megabytes. A heap dump contains most of
it too. Write the dumps to a disk that has room for them, and delete the old ones. A normal or triage dump is much
smaller, because it does not contain the heap.

## Share a dump

Any dump can contain sensitive data: what the process had in memory, the paths of its files. A triage dump removes the
personally identifiable information, at the cost of the heap:

<!-- ref:../../../samples/Meziantou.Framework.Diagnostics.MemoryDump.Samples/MemoryDumpExamples.cs;region=MemoryDump_WriteTriage;format=md-fenced-code-block;dedent=true -->
```csharp
// A small dump, without paths and other personally identifiable information, to send to someone else
var path = Path.Combine(Path.GetTempPath(), $"myapp-{Environment.ProcessId}.dmp");
MemoryDump.Write(path, MemoryDumpType.Triage);
```
<!-- endref -->

The removal is done by the code that writes the dump, `MiniDumpWriteDump` on Windows and `createdump` on Linux and
macOS, and only covers what they recognize. Still treat a triage dump as sensitive.
