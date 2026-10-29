---
title: NativeAOT and trimming
description: Serialize TOML in NativeAOT and trimmed applications, and remove the reflection-based code from the output.
---

The package is trimmable and compatible with NativeAOT. Reflection-based serialization is not: its methods are
annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the compiler warns when you call them in
such an application.

## Use source-generated metadata

Declare a `TomlSerializerContext` for your types, and call the overloads that take a `TomlTypeInfo<T>` or the context.
The overloads that take a `TomlSerializerOptions` are the reflection-based ones:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializerContext_Use;format=md-fenced-code-block;dedent=true -->
```csharp
var config = new ServerConfig { Port = 443 };
var toml = TomlSerializer.Serialize(config, ServerContext.Default.ServerConfig);
Assert.Equal("host = \"localhost\"\nport = 443\n", toml);

var roundTrip = TomlSerializer.Deserialize(toml, ServerContext.Default.ServerConfig);
Assert.Equal(443, roundTrip!.Port);

// APIs taking a Type accept a context
var value = TomlSerializer.Deserialize(toml, typeof(ServerConfig), ServerContext.Default);
Assert.IsType<ServerConfig>(value);
```
<!-- endref -->

See [Source generation](05-SourceGeneration.md) for how to declare the context.

## Disable reflection

An application that only uses source-generated metadata can turn reflection-based serialization off with the
`MeziantouFrameworkTomlIsReflectionEnabledByDefault` MSBuild property:

```xml
<PropertyGroup>
  <MeziantouFrameworkTomlIsReflectionEnabledByDefault>false</MeziantouFrameworkTomlIsReflectionEnabledByDefault>
</PropertyGroup>
```

- The property is a feature switch of the trimmer: the reflection-based code is removed from the trimmed output.
- It defaults to `false` when `PublishAot` or `NativeAot` is `true`. Set it explicitly to override that.
- It is published as the `Meziantou.Framework.Toml.TomlSerializer.IsReflectionEnabledByDefault` runtime configuration
  option. It can also be set with `AppContext.SetSwitch` before the serializer is first used.
- `TomlSerializer.IsReflectionEnabledByDefault` returns the value in effect.

When reflection is disabled, typed serialization needs a context. The built-in scalar types and the
[document model](07-DocumentModel.md) keep working without one.
