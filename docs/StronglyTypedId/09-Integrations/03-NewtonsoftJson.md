---
title: Newtonsoft.Json
description: Serialize IDs as their values with Newtonsoft.Json.
---

## Serialize an ID

When the project references Newtonsoft.Json, the generator adds a Newtonsoft.Json converter to each ID. It writes the
value of the ID, like the [System.Text.Json converter](02-SystemTextJson.md):

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_JsonModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record OrderLine(ProductId ProductId, ProjectId ProjectId, int Quantity);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_NewtonsoftJson;format=md-fenced-code-block;dedent=true -->
```csharp
var line = new OrderLine(ProductId.FromGuid(new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")), ProjectId.FromInt32(42), Quantity: 3);

var json = Newtonsoft.Json.JsonConvert.SerializeObject(line);
Assert.Equal("""{"ProductId":"0f8fad5b-d9cb-469f-a165-70867728950e","ProjectId":42,"Quantity":3}""", json);

var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<OrderLine>(json);
Assert.Equal(line, deserialized);
```
<!-- endref -->

The converter is registered with `[Newtonsoft.Json.JsonConverter]` on the ID: there is nothing to add to the
`JsonSerializerSettings`.

## Null values and the object form

- A `null` ID of a class type, and a `null` nullable struct (`ProjectId?`), are written and read as `null`.
  Reading `null` into a non-nullable struct ID throws a `JsonSerializationException`.
- An object that has a `Value` property is read as the ID, and its other properties are ignored. An object without a
  `Value` property gives an ID with the default value.
- `Int128` and `UInt128` are written as numbers with all their digits, and read from numbers or strings.
