---
title: Capturing a snapshot
description: Pick the information to capture, add values of your own, read the result and serialize it to JSON.
---

## Pick the information you need

[AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault) adds everything. To keep the snapshot small, or to avoid information you do not want to collect, call
the `Add*` methods you need instead. Each one adds one key to the snapshot and returns the builder, so the calls can
be chained:

<!-- ref:../../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=ContextSnapshot_SelectParts;format=md-fenced-code-block;dedent=true -->
```csharp
var snapshot = new ContextSnapshotBuilder()
    .AddOperatingSystem()
    .AddDotnetRuntime()
    .AddProcessorCount()
    .AddValue("Application.Version", "1.2.3")
    .BuildSnapshot();

// The keys are sorted in ordinal order
Assert.Equal(["Application.Version", "Dotnet", "OperatingSystem", "ProcessorCount"], snapshot.Keys);
Assert.Equal("1.2.3", snapshot["Application.Version"]);

// Each value is an object you can read, or serialize with the rest of the snapshot
var operatingSystem = Assert.IsType<OperatingSystemSnapshot>(snapshot["OperatingSystem"]);
Assert.Equal(OperatingSystem.IsWindows(), operatingSystem.IsWindows);
```
<!-- endref -->

- [BuildSnapshot](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.BuildSnapshot) returns an `IReadOnlyDictionary<string, object?>`. The keys are sorted in ordinal order, so two
  snapshots serialized to JSON can be compared line by line.
- Each value is a snapshot object, such as [OperatingSystemSnapshot](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.OperatingSystemSnapshot), or a plain value such as a number or a string.
  The keys and the types are listed in [What a snapshot contains](03-WhatASnapshotContains.md).
- [BuildSnapshot](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.BuildSnapshot) can be called several times. Each call returns a new dictionary; the builder keeps its values.

## Add your own values

`AddValue(key, value)` adds any value to the snapshot: the version of your application, the name of the environment,
the configuration of a feature. Adding a value with a key that already exists replaces the previous value, including
a value added by an `Add*` method.

Choose keys that cannot collide with the keys of the package, for instance by prefixing them with the name of your
application, and values that your serializer can write.

## Information that is not in AddDefault

Two methods are not called by [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault):

| Method | Key | Content |
| --- | --- | --- |
| [AddDotnetRuntime](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDotnetRuntime) | `Dotnet` | The runtime identifier, the framework description, and whether the runtime is Mono |
| [AddCommonAppContext](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddCommonAppContext) | `AppContext.<name>` | A fixed list of well-known `AppContext` switches and data, only those that are set |

[AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault) calls [AddAppContextData](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddAppContextData), which already adds every `AppContext` switch and data value of the process, so
[AddCommonAppContext](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddCommonAppContext) is only useful without [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault). [AddAppContextData](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddAppContextData) reads the private fields of
`AppContext` by reflection; when they are not found, it adds nothing.

## Serialize the snapshot

The snapshot is made of objects with public properties, dictionaries and immutable arrays, so `System.Text.Json`
serializes it with the options you already use, including `JsonSerializerDefaults.Strict` as in the
[overview](01-Overview.md):

<!-- ref:../../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=ContextSnapshot_Serialize;format=md-fenced-code-block;dedent=true -->
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

Add a `JsonStringEnumConverter` to write the enumeration values, such as [OSArchitecture](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.OperatingSystemSnapshot.OSArchitecture) or [DriveType](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.DriveSnapshot.DriveType), as names:
without it, they are written as numbers. The values are declared as `object`, so the serializer writes each one with
the properties of its actual type.

Values that the platform does not provide are `null`, and written as `null` in the JSON. For instance, the power
management scheme and the hypervisor are only captured on Windows, and the Linux distribution name only on Linux.
