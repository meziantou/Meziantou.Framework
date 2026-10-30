---
title: Source generation
description: When to generate the serialization code at build time with a TomlSerializerContext, how to declare and use one, and the diagnostics it reports.
---

By default, the serializer discovers your types with reflection the first time it sees them. The source generator does
this work when you build instead: it writes the code that reads and writes your types into a `TomlSerializerContext`.
The generator ships in the `Meziantou.Framework.Toml` package: there is nothing else to install.

## When to use it

Use a source-generated context when:

- **The application is published with NativeAOT or trimmed.** Reflection-based serialization is not compatible with
  them: the methods that use it are annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the
  compiler warns when you call them. See [NativeAOT and trimming](06-NativeAotAndTrimming.md).
- **You want errors when you build.** A type the serializer cannot handle, a dictionary with non-string keys, or an
  invalid converter is a compiler error, not an exception the first time a file is read.
- **Startup matters.** The metadata of the types is not discovered with reflection at runtime, and the naming policy is
  applied when building.

Reflection remains the simplest choice for an application that is neither trimmed nor published with NativeAOT, and
for types that are only known at runtime.

## Declare a context

Declare a `partial` class that derives from `TomlSerializerContext`, with a `[TomlSerializable]` attribute for each
type you pass to the serializer. The types reachable from these roots, such as the types of their properties, are
discovered automatically:

<!-- ref:../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializerContext_Declare;format=md-fenced-code-block;dedent=true -->
```csharp
[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(ServerConfig))]
internal sealed partial class ServerContext : TomlSerializerContext;

public sealed class ServerConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
}
```
<!-- endref -->

`TomlSerializerContext` and the attributes are in the [Meziantou.Framework.Toml.Serialization](cref:Meziantou.Framework.Toml.Serialization) namespace.

## Use the context

The generator adds a static `Default` instance to the context, and a `TomlTypeInfo<T>` property for each root type.
Pass the property, or the context, to the serializer:

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

- `Serialize`, `Deserialize`, `TryDeserialize` and their asynchronous, stream and reader variants all accept a
  `TomlTypeInfo<T>` or a context.
- The property is named after the type. `[TomlSerializable(typeof(T), TypeInfoPropertyName = "...")]` renames it.
  When two types have the same name, or a type is named like a member of the context, the property uses the
  namespace-qualified name of the type (for example `B_Item`).

## Set the options

The options of a context are set with `[TomlSourceGenerationOptions]`, as `ServerContext` does above. It sets most of
the [options](04-Serialization/03-Options.md) of `TomlSerializerOptions`: the naming policies are values of
`TomlKnownNamingPolicy`, and the converters are types.

The generated code applies these options when it is built: the member names, the members to serialize
(`IncludeFields`, `IgnoreReadOnlyFields`, `IgnoreReadOnlyProperties`) and the validation (`UnmappedMemberHandling`,
`RespectRequiredConstructorParameters`, `RespectNullableAnnotations`) cannot be changed at runtime. `context.Options`
returns the options of the context.

## What the generated code supports

The generated code honors the same attributes as reflection. Some points are specific to it:

- `init` and `required` members are supported. A class is created with its parameterless constructor, so its members
  keep their initial values and can be populated. A struct, a generic type, or a type created with
  a constructor that has parameters is created with an object initializer instead: its `required` members are replaced,
  and set to `default` when the document does not contain them.
- `[TomlConverter]` on a type or a member is supported, including converter factories. The converter type must be
  accessible from the context: public, or internal to the same assembly. A member with a converter is always replaced,
  never populated.
- The converters listed in `[TomlSourceGenerationOptions(Converters = [...])]` are chosen when building, so a converter
  factory listed there is not used (`MFTOML012`). Apply `[TomlConverter]` to the type or the member instead. See
  [Custom converters](04-Serialization/05-Converters.md#register-a-converter).
- Derived types that the base type cannot reference are registered with `[TomlDerivedTypeMapping]` on the context. See
  [Polymorphism](04-Serialization/06-Polymorphism.md#register-derived-types-from-another-project).

## Diagnostics

The generator reports the problems it finds as compiler diagnostics:

| Id | Severity | Reported when |
| --- | --- | --- |
| `MFTOML001` | Error | The context, or a type that contains it, is not `partial`. |
| `MFTOML002` | Error | A converter type is invalid. |
| `MFTOML003` | Error | A member uses a type the generator cannot serialize. Write a converter for it. |
| `MFTOML004` | Error | A dictionary member has keys that are not strings. |
| `MFTOML005` | Error | A `[TomlSourceGenerationOptions]` value is invalid, or a `TypeInfoPropertyName` is not a valid identifier, is used for two types, or clashes with a member of the context. |
| `MFTOML006` | Error | An extension data member is invalid. |
| `MFTOML007` | Error | A polymorphism configuration is invalid. |
| `MFTOML009` | Error | A `[TomlDerivedTypeMapping]` is invalid. |
| `MFTOML010` | Warning | The base type of a `[TomlDerivedTypeMapping]` has no polymorphism configuration; the defaults of the serializer are used. |
| `MFTOML011` | Error | A TOML attribute is used on a member it does not apply to. |
| `MFTOML012` | Warning | A converter factory listed in `[TomlSourceGenerationOptions(Converters)]` is not used by the generated code. |
| `MFTOML013` | Error | A constructor marked with `[TomlConstructor]` is private or protected. |
| `MFTOML014` | Error | The context is generic. It can be nested in a generic type. |
| `MFTOML015` | Error | A type the generated code uses is not accessible from the context, is less accessible than the context, or is file-local. |
| `MFTOML016` | Error | A member is a `ref struct`, a delegate or a pointer. |
| `MFTOML017` | Error | A root type cannot be serialized: an abstract class or an interface without polymorphism configuration, an open generic type, a multi-dimensional array, a delegate, a `ref struct` or a pointer. |
| `MFTOML018` | Error | A TOML attribute has an undefined enum value, such as `[TomlIgnore(Condition = (TomlIgnoreCondition)42)]`. |

The context has a public property for each type, so every type it uses must be accessible wherever the context is: an
`internal` type cannot be used by a `protected` nested context, which derived types in other assemblies can access
(`MFTOML015`).
