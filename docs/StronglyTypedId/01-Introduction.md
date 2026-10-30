---
title: Introduction
description: A source generator that turns a partial struct or class into a strongly-typed ID, with equality, parsing, formatting and converters.
---

`Meziantou.Framework.StronglyTypedId` is a C# source generator. You declare a `partial` struct or class with the
`[StronglyTypedId]` attribute and the type of its value, and the generator writes the rest at compile time: the
value, the factory method, equality, parsing, formatting and the converters that serializers and frameworks need.

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Declaration;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>]
public partial struct ProjectId { }
```
<!-- endref -->

A method that takes a `ProjectId` cannot receive a `CustomerId` or a plain `int` by mistake: the compiler rejects
it. The ID is still stored, serialized and bound as its underlying value: `42` in JSON, in a database column or in a
URL.

## What you can do with it

- **Declare IDs** for any of the [supported underlying types](04-UnderlyingTypes.md): integers, `Guid`, `string`,
  dates, MongoDB `ObjectId`... See [Declaring IDs](03-DeclaringIds.md).
- **Create, compare, parse and format IDs** with the [generated members](05-GeneratedMembers.md): `FromInt32`,
  `New()` for `Guid`, `==`, `Parse`, `TryParse`, `IParsable<T>`, `ISpanFormattable`...
- **Validate the values** and replace any generated member by your own. See
  [Validation and custom members](06-ValidationAndCustomMembers.md).
- **Serialize and bind IDs** with System.Text.Json, Newtonsoft.Json, MongoDB, `TypeConverter`, ASP.NET Core and
  Entity Framework Core. See [Integrations](09-Integrations/01-Overview.md).
- **Configure the generated code** per type or for the whole assembly. See [Options](07-Options.md) and
  [Assembly-wide defaults](08-AssemblyDefaults.md).
- **Write code that works with any ID** through the `IStronglyTypedId` interfaces. See
  [The interfaces package](10-Interfaces.md).

## Principles

- **Everything happens at compile time.** The generated code is plain C# in your assembly: no reflection, and no
  runtime dependency on the generator. The attributes are removed from the compiled assembly.
- **An ID is its value.** Two IDs are equal when their values are equal, and every converter reads and writes the
  bare value, not an object that contains it.
- **Culture never changes the result.** Parsing and formatting always use the invariant culture.
- **Your code wins.** When you write a member that the generator would generate, such as the constructor or
  `ToString`, the generator uses yours instead.
- **Converters follow your references.** A converter is generated only when the project can use it: the Newtonsoft.Json
  converter needs a reference to Newtonsoft.Json, the MongoDB serializer one to MongoDB.Bson.

## Packages and namespaces

| Package | Contains | Namespace |
| --- | --- | --- |
| `Meziantou.Framework.StronglyTypedId` | The source generator, its analyzer and the attributes | `Meziantou.Framework.Annotations` |
| `Meziantou.Framework.StronglyTypedId.Interfaces` | `IStronglyTypedId` and `IStronglyTypedId<T>` (optional) | [Meziantou.Framework](cref:Meziantou.Framework) |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and declare your first ID.
- [Generated members](05-GeneratedMembers.md): what the generator writes for you.
- [Limitations](12-Limitations.md): what the generator does not do.
