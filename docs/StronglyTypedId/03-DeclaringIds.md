---
title: Declaring IDs
description: Declare an ID with the generic attribute or with typeof, as a struct, a class or a record, and where it can be declared.
---

## Use the generic attribute

`[StronglyTypedId<T>]` takes the underlying type as its type argument:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Declaration;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>]
public partial struct ProjectId { }
```
<!-- endref -->

The generic attribute needs C# 11 or later and a project that targets .NET 10 or later.

## Use typeof on older targets

On .NET Framework, .NET Standard and the versions of .NET before .NET 10, or before C# 11, pass the type to the
non-generic attribute:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_DeclarationTypeof;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId(typeof(int))]
public partial struct CustomerId { }
```
<!-- endref -->

Both attributes take the same [options](07-Options.md) and generate the same code.

## Choose a struct, a class or a record

The attribute applies to a `partial` struct, class, record or record struct:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_DeclarationKinds;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<string>]
public sealed partial class Sku { }

[StronglyTypedId<long>]
public partial record struct TrackingNumber { }
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_DeclarationKindsUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var sku = Sku.FromString("ABC-123");
Assert.Equal("ABC-123", sku.Value);

var trackingNumber = TrackingNumber.FromInt64(1234567890);
Assert.Equal(1234567890L, trackingNumber.Value);
```
<!-- endref -->

- **A struct** is the usual choice: it does not allocate, and it cannot be `null`. Its `default` value wraps the
  default value of the underlying type (`0`, `Guid.Empty` or a `null` string), without calling the constructor.
- **A class** can be `null`, which a converter reads and writes as `null`. The generated `==` and `!=` operators
  accept `null`. The constructor is `private` in a `sealed` class and `protected` otherwise, so a non-sealed ID can be
  derived from.
- **A record or a record struct** keeps the members that the compiler writes for records: `ToString`, `Equals`,
  `GetHashCode` and the `==` and `!=` operators. The generator writes the rest, such as `Value`, `FromInt64`, the
  parsing methods and the converters.

## Where an ID can be declared

An ID can be declared in a namespace, in the global namespace, or nested in another type. When it is nested, every
containing type must be `partial` too, because the generated code is written inside them.

An ID cannot be generic, nor nested in a generic type: the analyzer reports
[MFSTID0004](11-Diagnostics.md#mfstid0004-generic-types-are-not-supported) and no code is generated.

## Implement IComparable to sort IDs

IDs are not comparable by default: an order between two IDs often has no meaning. To sort them, declare one of
`IComparable` or `IComparable<T>`:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Comparable;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>]
public partial struct Priority : IComparable { }
```
<!-- endref -->

The generator implements both interfaces and adds the `<`, `<=`, `>` and `>=` operators:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_ComparableUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var low = Priority.FromInt32(1);
var high = Priority.FromInt32(2);

// IComparable<Priority> and the comparison operators are generated
Assert.True(low < high);
Assert.True(high >= low);
Assert.Equal(-1, low.CompareTo(high));
```
<!-- endref -->

IDs are compared by their values with `Comparer<T>.Default` of the underlying type. For a `string`, this comparison
depends on the current culture and ignores the [`StringComparison` option](07-Options.md#compare-string-values),
which only applies to equality.
