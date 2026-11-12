---
title: TOML
description: Serialize IDs as their values with Meziantou.Framework.Toml, and what to do with a source-generated context.
---

## Serialize an ID

When the project references Meziantou.Framework.Toml, the generator adds a TOML converter to each ID and registers it
with `[TomlConverter]`. It writes the value of the ID:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_JsonModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record OrderLine(ProductId ProductId, ProjectId ProjectId, int Quantity);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Toml;format=md-fenced-code-block;dedent=true -->
```csharp
var line = new OrderLine(ProductId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")), ProjectId.FromInt32(42), Quantity: 3);

var toml = Meziantou.Framework.Toml.TomlSerializer.Serialize(line);
Assert.Equal("""
    ProductId = "0f8fad5b-d9cb-469f-a165-70867728950e"
    ProjectId = 42
    Quantity = 3

    """, toml);

var deserialized = Meziantou.Framework.Toml.TomlSerializer.Deserialize<OrderLine>(toml);
Assert.Equal(line, deserialized);
```
<!-- endref -->

## How values are written

The TOML type of the value depends on the [underlying type](../04-UnderlyingTypes.md#supported-types):

| Underlying type | TOML value |
| --- | --- |
| `bool` | A boolean. |
| `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long` | An integer. Reading a value out of the range of the type throws a [TomlException](cref:Meziantou.Framework.Toml.TomlException). |
| `ulong`, `Int128`, `UInt128`, `BigInteger` | An integer when the value fits in a TOML integer (a signed 64-bit integer), a string otherwise. Both are read. |
| `Half`, `float`, `double` | A float. Integers are read too. |
| `decimal` | A float with all the digits of the value. Integers are read too. |
| `string`, `Guid`, `ObjectId` | A string. |
| `DateTime` | An offset date-time with `Z` for a UTC value, with the offset of the machine for a local value, and a local date-time otherwise. A `Z` offset is read as a UTC value, another offset as a local value, and a local date-time or a local date as an unspecified value. |
| `DateTimeOffset` | An offset date-time. A local date-time, date or time cannot be read. |

TOML has no `null` value. The serializer omits a property whose value is `null`, and the converter throws a
[TomlException](cref:Meziantou.Framework.Toml.TomlException) when it is asked to write a `null` ID or an ID whose `string` value is `null`.

The keys of a TOML dictionary are strings: an ID cannot be the key of a dictionary.

## Source generation and Native AOT

A source-generated [TomlSerializerContext](cref:Meziantou.Framework.Toml.Serialization.TomlSerializerContext) does not see the `[TomlConverter]` attribute that this generator adds to
the ID: the TOML source generator cannot see the code of other source generators.

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_TomlSourceGenerationModel;format=md-fenced-code-block;dedent=true -->
```csharp
[Meziantou.Framework.Toml.Serialization.TomlSerializable(typeof(OrderLine))]
internal sealed partial class OrderTomlContext : Meziantou.Framework.Toml.Serialization.TomlSerializerContext;
```
<!-- endref -->

Register the [generated converters](01-Overview.md#use-a-converter-directly) in options whose
[TypeInfoResolver](cref:Meziantou.Framework.Toml.TomlSerializerOptions.TypeInfoResolver) is the context, and get the metadata of the type from these options:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_TomlSourceGeneration;format=md-fenced-code-block;dedent=true -->
```csharp
// The generated converters are public: register them in options that use the context
var options = new Meziantou.Framework.Toml.TomlSerializerOptions
{
    TypeInfoResolver = OrderTomlContext.Default,
    Converters = [new ProductId.ProductIdTomlConverter(), new ProjectId.ProjectIdTomlConverter()],
};
var typeInfo = (Meziantou.Framework.Toml.TomlTypeInfo<OrderLine>)options.TypeInfoResolver.GetTypeInfo(typeof(OrderLine), options)!;

var line = new OrderLine(ProductId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")), ProjectId.FromInt32(42), Quantity: 3);
var toml = Meziantou.Framework.Toml.TomlSerializer.Serialize(line, typeInfo);
Assert.Equal("""
    ProductId = "0f8fad5b-d9cb-469f-a165-70867728950e"
    ProjectId = 42
    Quantity = 3

    """, toml);

var deserialized = Meziantou.Framework.Toml.TomlSerializer.Deserialize(toml, typeInfo);
Assert.Equal(line, deserialized);
```
<!-- endref -->

Add one converter per ID type that the serialized types contain.
