---
title: Getting started
description: Install the package, declare an ID, then create, compare, parse and serialize it.
---

## Install the package

Add the `Meziantou.Framework.StronglyTypedId` package to your project:

```bash
dotnet add package Meziantou.Framework.StronglyTypedId
```

The package contains the source generator and the attributes. When the project enables implicit usings
(`<ImplicitUsings>enable</ImplicitUsings>`), the package also adds `global using Meziantou.Framework.Annotations;`;
otherwise, add the `using` directive yourself.

The examples of this documentation omit the `using` directives. They check their results with assertions such as
`Assert.Equal`, which show what each call returns.

## Declare an ID

Add `[StronglyTypedId<T>]` to a `partial` struct, where `T` is the type of the value:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Declaration;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>]
public partial struct ProjectId { }
```
<!-- endref -->

The type must be `partial`, because the generator adds members to it. The body stays empty: everything else is
generated when the project builds.

## Use it

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var id = ProjectId.FromInt32(42);
Assert.Equal(42, id.Value);
Assert.Equal("42", id.ValueAsString);
Assert.Equal("ProjectId { Value = 42 }", id.ToString());

// Two ids are equal when their values are equal
Assert.Equal(ProjectId.FromInt32(42), id);

Assert.Equal(id, ProjectId.Parse("42"));
Assert.True(ProjectId.TryParse("42", out var parsed));
Assert.Equal(id, parsed);
Assert.False(ProjectId.TryParse("abc", out _));
```
<!-- endref -->

- `FromInt32` creates an ID. The name of the method follows the underlying type: `FromGuid`, `FromString`,
  `FromInt64`... The constructor is private.
- `Value` is the underlying value, and [ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString) the value formatted with the invariant culture.
- `ToString()` shows the type and the value, like a record. You can get the bare value instead: see
  [Options](07-Options.md#format-tostring-as-the-value).
- Equality compares the values, and `Parse` and `TryParse` read the string representation.

## Serialize it

An ID is serialized as its value, so an ID created from `42` becomes `42` in JSON:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedIdAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var userId = UserId.FromInt32(42);
var json = System.Text.Json.JsonSerializer.Serialize(userId);
Assert.Equal("42", json);
```
<!-- endref -->

The System.Text.Json converter is generated for every ID, and the Newtonsoft.Json and MongoDB converters when the
project references these libraries. See [Integrations](09-Integrations/01-Overview.md).

## Next steps

- [Declaring IDs](03-DeclaringIds.md): structs, classes and records, and the attribute for older C# versions.
- [Underlying types](04-UnderlyingTypes.md): the types an ID can wrap.
- [Validation and custom members](06-ValidationAndCustomMembers.md): reject invalid values.
