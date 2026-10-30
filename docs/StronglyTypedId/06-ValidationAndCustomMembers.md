---
title: Validation and custom members
description: Reject invalid values with your own constructor, and replace any generated member by your own implementation.
---

## Validate the value

To reject invalid values, write the constructor that takes the underlying value. Throw when the value is not valid, and
assign the generated `_value` field:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Validation;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>]
public partial struct OrderNumber
{
    // The generator uses this constructor instead of generating one
    private OrderNumber(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        _value = value;
    }
}
```
<!-- endref -->

The generator detects the constructor and does not generate one. Every generated member creates the ID through it:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_ValidationUsage;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(42, OrderNumber.FromInt32(42).Value);
Assert.Throws<ArgumentOutOfRangeException>("value", () => OrderNumber.FromInt32(0));

// TryParse returns false when the constructor throws, and Parse throws a FormatException
Assert.False(OrderNumber.TryParse("0", out _));
Assert.Throws<FormatException>(() => OrderNumber.Parse("0"));

// The default value of a struct does not call the constructor
Assert.Equal(0, default(OrderNumber).Value);
```
<!-- endref -->

- `FromInt32` throws the exception of the constructor.
- `TryParse` returns `false` when the constructor throws, and `Parse` throws a `FormatException`. A route or query
  string value that the constructor rejects is a bad request in [ASP.NET Core](09-Integrations/06-AspNetCore.md).
- The JSON, BSON and `TypeConverter` converters call the constructor too, so deserializing an invalid value throws.

Keep the constructor `private` so that `FromInt32` stays the only way to create an ID. The generator detects a
constructor of any accessibility.

`default(OrderNumber)` and the elements of a new `OrderNumber[]` array are never validated: C# creates the default value of
a struct without calling a constructor. Declare the ID as a `sealed partial class` if a missing ID must be `null`
rather than an ID with the value `0`.

## Replace a generated member

The generator skips every member that the type already declares with the same signature. Write your own version of:

- the `_value` field, `Value` and [ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString);
- the constructor that takes the underlying value;
- `ToString()`, `GetHashCode()`, `Equals(object?)`, `Equals(T)`, `==` and `!=`;
- `Parse` and `TryParse`, for `string`, `ReadOnlySpan<char>` and UTF-8 (`ReadOnlySpan<byte>`);
- `ToString(string?, IFormatProvider?)` and the `TryFormat` methods of `ISpanFormattable` and `IUtf8SpanFormattable`;
- `New()` for `Guid` IDs;
- `CompareTo` and the comparison operators of [comparable IDs](03-DeclaringIds.md#implement-icomparable-to-sort-ids).

The generated members that remain use yours: the interface implementations call your `Parse` and `TryParse`, and
formatting without a format calls your `ToString()`. The factory method (`FromInt32`) is always generated.

To add a member that the generator does not write, such as a constant or a domain method, write it in the type like in
any `partial` type.
