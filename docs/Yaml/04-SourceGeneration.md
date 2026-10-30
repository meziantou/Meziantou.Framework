---
title: Source generation
description: Generate the serialization metadata at build time, for NativeAOT and trimmed applications, and disable reflection.
---

By default, the serializer discovers your types with reflection when it first uses them. The source generator does this
work at build time instead: it writes the code that creates, reads and writes your types, in a class you declare. The
generator ships in the `Meziantou.Framework.Yaml` package; there is nothing else to install.

## When to use it

- **NativeAOT and trimming**: the trimmer cannot see which members reflection needs. With generated metadata, the
  application can be published with NativeAOT or trimming, and reflection can be [removed](#disable-reflection).
- **Errors at build time**: a type that cannot be serialized is reported by a diagnostic when you build, instead of an
  exception when the code runs.
- **No discovery at run time**: the metadata of your types is not built with reflection on first use.

The reflection-based serializer needs no declaration and handles any type, such as anonymous types or types known at
run time only. Keep it when you do not publish with NativeAOT or trimming and these benefits do not matter.

## Declare a context

Declare a `partial` class that derives from `YamlSerializerContext`, and add `[YamlSerializable]` for each type you
serialize or deserialize directly. Both are in the [Meziantou.Framework.Yaml.Serialization](cref:Meziantou.Framework.Yaml.Serialization) namespace. This context
handles the `Product` class of [Getting started](02-GettingStarted.md#serialize-a-class):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerSourceGenerationTests.cs;region=YamlSerializerContext_Declare;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlSerializable(typeof(Product))]
[YamlSourceGenerationOptions(
    PropertyNamingPolicy = YamlKnownNamingPolicy.KebabCaseLower,
    WriteIndented = true)]
public sealed partial class AppYamlContext : YamlSerializerContext
{
}
```
<!-- endref -->

The types used by the members of these types, such as the elements of a collection, are generated too: list only the
types you pass to the serializer. The generator adds a static `Default` instance to the context, and a property for each
type, named after the type. When two names collide, set `TypeInfoPropertyName` on `[YamlSerializable]` to rename the
property.

## Serialize with the context

Pass the property of the type to the serializer:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerSourceGenerationTests.cs;region=YamlSerializerContext_TypeInfo;format=md-fenced-code-block;dedent=true -->
```csharp
var product = new Product { Id = 1, DisplayName = "Sample product", Tags = ["new"] };

var yaml = YamlSerializer.Serialize(product, AppYamlContext.Default.Product);
Assert.Equal("id: 1\nname: Sample product\ntags:\n  - new\n", yaml);

var copy = YamlSerializer.Deserialize(yaml, AppYamlContext.Default.Product);
Assert.Equal("Sample product", copy!.DisplayName);
```
<!-- endref -->

Or pass the context, and let the serializer find the metadata of the type:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerSourceGenerationTests.cs;region=YamlSerializerContext_Context;format=md-fenced-code-block;dedent=true -->
```csharp
var product = new Product { Id = 1, DisplayName = "Sample product" };

var yaml = YamlSerializer.Serialize(product, AppYamlContext.Default);
var copy = YamlSerializer.Deserialize<Product>(yaml, AppYamlContext.Default);
Assert.Equal("Sample product", copy!.DisplayName);
```
<!-- endref -->

Serializing a type that the context does not know throws an `InvalidOperationException`.

## Options

`[YamlSourceGenerationOptions]` sets the options of the context, with most of the properties of
`YamlSerializerOptions`, including `Converters`. They are the options of `AppYamlContext.Default`.

To use the context with other options, such as a [`SourceName`](08-ErrorHandling.md), `CreateOptions` derives an
options instance that keeps the context as its metadata source:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerSourceGenerationTests.cs;region=YamlSerializerContext_CreateOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = AppYamlContext.Default.CreateOptions(o => o with { SourceName = "config.yaml" });
```
<!-- endref -->

You can also create a context with a `YamlSerializerOptions` instance. The generated code reads most options when it
runs, so other values are honored, except the options that shape the generated code:

- `PropertyNamingPolicy` is always applied at build time: the keys are generated from the policy of the attribute, and
  the policy of an options instance is ignored. `DictionaryKeyPolicy` is read at run time.
- `DefaultIgnoreCondition`, `IncludeFields`, `IgnoreReadOnlyFields`, `IgnoreReadOnlyProperties`,
  `UnmappedMemberHandling`, `PreferredObjectCreationHandling`, `DuplicateKeyHandling`, `MappingOrder`,
  `PropertyNameCaseInsensitive`, `DiscriminatorStyle`, `TypeDiscriminatorPropertyName`, `UnknownDerivedTypeHandling`,
  `InferClosedTypePolymorphism`, and whether the `Schema` applies merge keys are fixed at build time when the attribute
  sets them, and read from the options otherwise.

## What the generator supports

Besides the [supported types](03-Serialization/03-SupportedTypes.md) and the attributes, the generated code handles:

- non-public members marked with `[YamlInclude]` and non-public constructors marked with `[YamlConstructor]`, reached
  through `UnsafeAccessorAttribute`, which NativeAOT and trimming understand;
- [polymorphism](03-Serialization/08-Polymorphism.md): the derived types of `[YamlDerivedType]` and of closed
  hierarchies are generated. Declare the derived types that live in another assembly with `[YamlDerivedTypeMapping]` on
  the context.

## Disable reflection

An application that only uses generated metadata can remove the reflection-based serializer. Set this property in the
project file:

```xml
<PropertyGroup>
  <MeziantouFrameworkYamlIsReflectionEnabledByDefault>false</MeziantouFrameworkYamlIsReflectionEnabledByDefault>
</PropertyGroup>
```

The property is a feature switch: the trimmer removes the reflection code from the published application. It is
`false` by default when `PublishAot` is `true`; set it explicitly to override that. `YamlSerializer.IsReflectionEnabledByDefault`
returns the value at run time. When reflection is disabled, serializing a type without generated metadata throws an
`InvalidOperationException` that names the type.

## Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| `MFY001` | Error | The context type must be declared `partial`. |
| `MFY002` | Error | A member uses a type the generator cannot serialize. |
| `MFY003` | Error | An extension data member uses an unsupported type. |
| `MFY004` | Error | A type declares several `[YamlExtensionData]` members. |
| `MFY005` | Error | A `[YamlSourceGenerationOptions]` value is invalid. |
| `MFY006` | Error | A converter type is invalid. |
| `MFY007` | Error | A serializable type, or the element or key type of a serializable collection, is not supported. |
| `MFY020` | Error | `[YamlDerivedTypeMapping]` declares a type that is not assignable to the base type. |
| `MFY021` | Warning | The base type of a `[YamlDerivedTypeMapping]` has no `[YamlPolymorphic]`; the serializer defaults are used. |
| `MFY022` | Warning | An open generic derived type cannot be closed for the base type, and is ignored. |
| `MFY023` | Error | `InferClosedTypePolymorphism` is enabled on a type that is not `closed`. |
| `MFY024` | Warning | The explicit `[YamlDerivedType]` registrations of the type replace the inference. |
| `MFY025` | Warning | An inferred derived type is ignored, because of its visibility or a duplicate discriminator. |
| `MFY026` | Error | A type registers the same derived type, discriminator or tag twice, or two default derived types. |
