---
title: System.Text.Json
description: Serialize IDs as their values with System.Text.Json, use them as dictionary keys, and what to do with a source-generated context.
---

## Serialize an ID

The generated converter writes the value of the ID, not an object:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_JsonModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record OrderLine(ProductId ProductId, ProjectId ProjectId, int Quantity);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_SystemTextJson;format=md-fenced-code-block;dedent=true -->
```csharp
var line = new OrderLine(ProductId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")), ProjectId.FromInt32(42), Quantity: 3);

var json = System.Text.Json.JsonSerializer.Serialize(line);
Assert.Equal("""{"ProductId":"0f8fad5b-d9cb-469f-a165-70867728950e","ProjectId":42,"Quantity":3}""", json);

var deserialized = System.Text.Json.JsonSerializer.Deserialize<OrderLine>(json);
Assert.Equal(line, deserialized);
```
<!-- endref -->

The JSON type of the value depends on the [underlying type](../04-UnderlyingTypes.md#supported-types): a number for the
numeric types, a string for `Guid`, `string`, the dates and `ObjectId`, and `true` or `false` for `bool`.
`Int128`, `UInt128` and `BigInteger` are written as numbers with all their digits, and also read from strings.

A `null` ID of a class type is written as `null` and read back as `null`.

## Use IDs as dictionary keys

A dictionary keyed by an ID is written as a JSON object whose property names are the [ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString) of the IDs:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_SystemTextJsonDictionaryKey;format=md-fenced-code-block;dedent=true -->
```csharp
var budgets = new Dictionary<ProjectId, decimal> { [ProjectId.FromInt32(42)] = 1500m };

var json = System.Text.Json.JsonSerializer.Serialize(budgets);
Assert.Equal("""{"42":1500}""", json);

var deserialized = System.Text.Json.JsonSerializer.Deserialize<Dictionary<ProjectId, decimal>>(json);
Assert.Equal(1500m, deserialized![ProjectId.FromInt32(42)]);
```
<!-- endref -->

## Read the object form

The converter also reads an object that has a `Value` property, as an ID serialized without converter would look:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_SystemTextJsonObjectForm;format=md-fenced-code-block;dedent=true -->
```csharp
// An object with a "Value" property is also accepted; the other properties are ignored
var id = System.Text.Json.JsonSerializer.Deserialize<ProjectId>("""{"Value":42,"Name":"Documentation"}""");
Assert.Equal(ProjectId.FromInt32(42), id);
```
<!-- endref -->

An object without a `Value` property gives an ID with the default value. The converter always writes the value alone.

## Source generation and Native AOT

With a source-generated `JsonSerializerContext`, the generated converter is not used: an ID is written as an empty object
(`{}`). The System.Text.Json source generator cannot see the code of other source generators, so it does not know the
`[JsonConverter]` attribute that this generator adds to the ID. Use the reflection-based serializer (the default
`JsonSerializerOptions`) for the types that contain IDs. See [Limitations](../12-Limitations.md).
