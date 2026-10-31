---
title: Getting started
description: Install a package, capture a snapshot of the execution context as JSON, and write a memory dump of the current process.
---

## Install the packages

Add the packages to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Diagnostics.ContextSnapshot,Meziantou.Framework.Diagnostics.MemoryDump" />

## Capture the execution context

[ContextSnapshotBuilder](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder) collects the information, and [BuildSnapshot](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.BuildSnapshot) returns it as a dictionary that you can
serialize with `System.Text.Json`:

<!-- ref:../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=ContextSnapshot_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var snapshot = new ContextSnapshotBuilder().AddDefault().BuildSnapshot();

// You can serialize the context
var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
{
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    WriteIndented = true,
    Converters =
    {
        new JsonStringEnumConverter(),
    },
});
```
<!-- endref -->

- [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault) adds everything the package knows how to capture. You can also pick the parts you need: see
  [Capturing a snapshot](03-ContextSnapshot/02-CapturingASnapshot.md).
- `JsonStringEnumConverter` writes enumeration values, such as the architecture or the platform, as names instead of
  numbers.
- The examples need `using Meziantou.Framework.Diagnostics.ContextSnapshot;`, `using System.Text.Json;` and
  `using System.Text.Json.Serialization;`.

## Write a memory dump

[MemoryDump.Write](cref:Meziantou.Framework.Diagnostics.MemoryDump.Write) writes a dump of the current process to a file:

<!-- ref:../../samples/Meziantou.Framework.Diagnostics.MemoryDump.Samples/ReadmeExamples.cs;region=MemoryDump_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Full dump (default)
MemoryDump.Write("app.dmp");

// Smaller dump containing the managed heap
await MemoryDump.WriteAsync("app.dmp", MemoryDumpType.WithHeap, cancellationToken);
```
<!-- endref -->

- The default dump is a full dump, as large as the memory of the process. [MemoryDumpType.WithHeap](cref:Meziantou.Framework.Diagnostics.MemoryDumpType.WithHeap) is smaller and
  enough to inspect the managed heap. See [Dump types](04-MemoryDump/02-DumpTypes.md).
- The example needs `using Meziantou.Framework.Diagnostics;`.
- On Linux and macOS, the operating system can prevent the process from being dumped. On macOS in particular, an
  application started with its own executable (`./app`) cannot be dumped unless you re-sign it. See
  [Platform requirements](04-MemoryDump/03-PlatformRequirements.md).

## Next steps

- [Context snapshot](03-ContextSnapshot/01-Overview.md): what a snapshot contains and how to keep secrets out of it.
- [Memory dumps](04-MemoryDump/01-Overview.md): when to write a dump and how to open it.
