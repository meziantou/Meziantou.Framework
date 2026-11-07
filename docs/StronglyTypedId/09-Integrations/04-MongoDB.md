---
title: MongoDB
description: Store IDs in MongoDB documents as their values, including as the _id of a document.
---

## Store an ID

When the project references MongoDB.Bson, the generator adds a BSON serializer to each ID and registers it with
`[BsonSerializer]`. A property of an ID type is stored as the value:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_MongoDBModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ProjectDocument
{
    public ProjectId Id { get; set; }
    public string? Name { get; set; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_MongoDB;format=md-fenced-code-block;dedent=true -->
```csharp
var project = new ProjectDocument { Id = ProjectId.FromInt32(42), Name = "Documentation" };

var document = project.ToBsonDocument();
Assert.Equal(42, document["_id"].AsInt32);

var deserialized = BsonSerializer.Deserialize<ProjectDocument>(document);
Assert.Equal(ProjectId.FromInt32(42), deserialized.Id);
```
<!-- endref -->

An ID property named `Id` is the `_id` of the document, as for any other type.

## How values are stored

- The serializer writes the value with the serializer that MongoDB uses for the underlying type. Your registrations
  apply: MongoDB.Bson 3 needs a representation for `Guid` values, such as
  `BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard))`, and IDs that wrap a `Guid` use
  it.
- `Int128`, `UInt128` and `BigInteger` values are stored as strings, and `Half` values as `float` (BSON double).
- A `null` ID of a class type is stored as `null`.

## Use ObjectId values

An ID can wrap a `MongoDB.Bson.ObjectId`: `[StronglyTypedId<ObjectId>]`. It is stored as an `ObjectId`, and written
as a string in JSON. See [Underlying types](../04-UnderlyingTypes.md).
