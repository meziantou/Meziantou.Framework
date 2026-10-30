---
title: The interfaces package
description: Write code that works with any ID through IStronglyTypedId and IStronglyTypedId<T>.
---

## Install the package

The `Meziantou.Framework.StronglyTypedId.Interfaces` package contains two interfaces that the generated IDs implement.
Add it to your project:

```bash
dotnet add package Meziantou.Framework.StronglyTypedId.Interfaces
```

When the project references it, every ID implements:

| Interface | Members |
| --- | --- |
| `IStronglyTypedId` | `string ValueAsString { get; }` and `Type UnderlyingType { get; }` |
| `IStronglyTypedId<T>` | `T Value { get; }`, where `T` is the underlying type, and the members of `IStronglyTypedId` |

The interfaces are in the [Meziantou.Framework](cref:Meziantou.Framework.StronglyTypedId.Interfaces!N:Meziantou.Framework) namespace. You do not change the declaration of the IDs: the generator
adds the interfaces when it finds them.

## Use an ID without knowing its type

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedIdAttribute_Declaration;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId(typeof(int))]
public partial struct UserId
{
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Interfaces;format=md-fenced-code-block;dedent=true -->
```csharp
// The generated type implements IStronglyTypedId and IStronglyTypedId<int>
var userId = UserId.FromInt32(42);

// Access via interface
IStronglyTypedId stronglyTypedId = userId;
Assert.Equal("42", stronglyTypedId.ValueAsString);
Assert.Equal(typeof(int), stronglyTypedId.UnderlyingType);

// Access via generic interface
Assert.Equal(42, GetValue(userId));

static T GetValue<T>(IStronglyTypedId<T> id) => id.Value;
```
<!-- endref -->

- `IStronglyTypedId` gives the value as a string and its type, for code that handles every ID the same way: logging,
  auditing, building cache keys or URLs.
- `IStronglyTypedId<T>` gives the typed value, for generic code that needs it: `IStronglyTypedId<int>` matches every
  ID that wraps an `int`. `T` is the underlying type, not the type of the ID.
- `UnderlyingType` is implemented explicitly: it is only visible through the interface.

Accessing an ID of a struct type through an interface boxes it. In generic code, use a constraint
(`where TId : IStronglyTypedId<int>`) to avoid the allocation.
