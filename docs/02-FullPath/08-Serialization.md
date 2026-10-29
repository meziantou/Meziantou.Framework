---
title: Serialization
description: Read and write FullPath values in JSON with System.Text.Json, including with the source generator and NativeAOT.
---

## JSON

`FullPath` is serialized as a JSON string by `FullPathJsonConverter`. The converter is declared on the type, so there
is nothing to register:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_Json;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath output = FullPath.GetTempPath() / "output";

// A FullPath is written as a JSON string
string json = JsonSerializer.Serialize(output);
Assert.Equal(JsonSerializer.Serialize(output.Value), json);
Assert.Equal(output, JsonSerializer.Deserialize<FullPath>(json));

// A relative path is resolved against the current directory, and an empty string gives the empty path
Assert.Equal(FullPath.CurrentDirectory() / "output", JsonSerializer.Deserialize<FullPath>("\"output\""));
Assert.True(JsonSerializer.Deserialize<FullPath>("\"\"").IsEmpty);
```
<!-- endref -->

- `null` and an empty string give the empty path, and the empty path is written as an empty string.
- A relative path is resolved against the current directory when the document is read. To resolve the paths of a
  configuration file against its own folder, read them as strings and combine them with the folder of the file.
- A string that is not a valid path throws a `JsonException`. The original exception is its `InnerException`; the
  message does not contain the path.

## Source generator, trimming and NativeAOT

The converter works with the `System.Text.Json` source generator. Declare a `FullPath` property like any other:

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_JsonSourceGenerator;format=md-fenced-code-block;dedent=true -->
```csharp
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Configuration))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

internal sealed record Configuration(FullPath OutputPath);
```
<!-- endref -->

The package is trimmable and compatible with NativeAOT: it does not use reflection, and its native interop is generated
at compile time.
