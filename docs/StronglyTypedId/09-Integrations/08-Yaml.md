---
title: YAML
description: Serialize IDs as their values with Meziantou.Framework.Yaml, and what to do with a source-generated context.
---

## Serialize an ID

When the project references Meziantou.Framework.Yaml, the generator adds a YAML converter to each ID and registers it
with `[YamlConverter]`. It writes the value of the ID:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_JsonModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record OrderLine(ProductId ProductId, ProjectId ProjectId, int Quantity);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Yaml;format=md-fenced-code-block;dedent=true -->
```csharp
var line = new OrderLine(ProductId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")), ProjectId.FromInt32(42), Quantity: 3);

var yaml = Meziantou.Framework.Yaml.YamlSerializer.Serialize(line);
Assert.Equal("""
    ProductId: 0f8fad5b-d9cb-469f-a165-70867728950e
    ProjectId: 42
    Quantity: 3

    """, yaml);

var deserialized = Meziantou.Framework.Yaml.YamlSerializer.Deserialize<OrderLine>(yaml);
Assert.Equal(line, deserialized);
```
<!-- endref -->

## How values are written

- The converter reads and writes the value with the converter of the underlying type, so an ID is written like its
  value. The converters of the [YamlSerializerOptions](cref:Meziantou.Framework.Yaml.YamlSerializerOptions) apply: a converter registered for `int` also converts
  the IDs that wrap an `int`.
- A `MongoDB.Bson.ObjectId` value is written as a string.
- A `null` ID of a class type, and a `null` nullable struct (`ProjectId?`), are written and read as `null`.

## Use IDs as dictionary keys

A dictionary keyed by an ID is written as a mapping whose keys are the values of the IDs:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_YamlDictionaryKey;format=md-fenced-code-block;dedent=true -->
```csharp
var budgets = new Dictionary<ProjectId, decimal> { [ProjectId.FromInt32(42)] = 1500m };

var yaml = Meziantou.Framework.Yaml.YamlSerializer.Serialize(budgets);
Assert.Equal("""
    42: 1500

    """, yaml);

var deserialized = Meziantou.Framework.Yaml.YamlSerializer.Deserialize<Dictionary<ProjectId, decimal>>(yaml);
Assert.Equal(1500m, deserialized![ProjectId.FromInt32(42)]);
```
<!-- endref -->

A source-generated context does not support these dictionaries: the YAML source generator reports `MFY002` for a
dictionary whose keys are not a built-in scalar type.

## Source generation and Native AOT

A source-generated [YamlSerializerContext](cref:Meziantou.Framework.Yaml.Serialization.YamlSerializerContext) does not see the `[YamlConverter]` attribute that this generator adds to
the ID: the YAML source generator cannot see the code of other source generators. Register the
[generated converters](01-Overview.md#use-a-converter-directly) in the options of the context instead. Declare a
constructor that takes the options:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_YamlSourceGenerationModel;format=md-fenced-code-block;dedent=true -->
```csharp
[Meziantou.Framework.Yaml.Serialization.YamlSerializable(typeof(OrderLine))]
internal sealed partial class OrderYamlContext : Meziantou.Framework.Yaml.Serialization.YamlSerializerContext
{
    public OrderYamlContext(Meziantou.Framework.Yaml.YamlSerializerOptions options)
        : base(options)
    {
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_YamlSourceGeneration;format=md-fenced-code-block;dedent=true -->
```csharp
// The generated converters are public: register them in the options of the context
var options = new Meziantou.Framework.Yaml.YamlSerializerOptions
{
    Converters = [new ProductId.ProductIdYamlConverter(), new ProjectId.ProjectIdYamlConverter()],
};
var context = new OrderYamlContext(options);

var line = new OrderLine(ProductId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")), ProjectId.FromInt32(42), Quantity: 3);
var yaml = Meziantou.Framework.Yaml.YamlSerializer.Serialize(line, context.StronglyTypedIdTestsOrderLine);
Assert.Equal("""
    ProductId: 0f8fad5b-d9cb-469f-a165-70867728950e
    ProjectId: 42
    Quantity: 3

    """, yaml);

var deserialized = Meziantou.Framework.Yaml.YamlSerializer.Deserialize(yaml, context.StronglyTypedIdTestsOrderLine);
Assert.Equal(line, deserialized);
```
<!-- endref -->

Add one converter per ID type that the serialized types contain.
