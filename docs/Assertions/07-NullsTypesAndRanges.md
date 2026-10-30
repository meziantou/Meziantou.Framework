---
title: Nulls, types and ranges
description: Check conditions, null values, the runtime type of a value, and whether a value is within a range.
---

## Conditions and null values

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertNullTests.cs;region=Assert_NullAndNotNull;format=md-fenced-code-block;dedent=true -->
```csharp
static string? FindUser(int id) => id == 1 ? "Alice" : null;

Assert.Null(FindUser(2));

var user = Assert.NotNull(FindUser(1));     // returns the value as a non-nullable string
Assert.Equal("ALICE", user.ToUpperInvariant());

Assert.True(char.IsUpper(user[0]));
Assert.False(string.IsNullOrWhiteSpace(user));
```
<!-- endref -->

- [True](cref:Meziantou.Framework.Assertions.Assert.True) and [False](cref:Meziantou.Framework.Assertions.Assert.False) check a `bool` or a `bool?`. Prefer a specialized assertion when there is one: the failure message
  of `Assert.True(list.Contains(item))` only says that the condition is false, while `Assert.Contains(item, list)` shows
  the list. The [analyzer](11-AnalyzerRules.md) suggests the specialized assertion.
- [Null](cref:Meziantou.Framework.Assertions.Assert.Null) and [NotNull](cref:Meziantou.Framework.Assertions.Assert.NotNull) accept references and nullable value types. [NotNull](cref:Meziantou.Framework.Assertions.Assert.NotNull) returns the value with its non-nullable
  type, so the rest of the test needs no `!`.
- A non-nullable value type is never `null`: the analyzer reports [Null](cref:Meziantou.Framework.Assertions.Assert.Null) and [NotNull](cref:Meziantou.Framework.Assertions.Assert.NotNull) on one as an error (`MFAS0008`,
  `MFAS0009`).

## Types

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertIsTypeTests.cs;region=Assert_Types;format=md-fenced-code-block;dedent=true -->
```csharp
object value = new List<int> { 1, 2 };

var list = Assert.IsType<List<int>>(value);                 // exactly this type, and returned as it
Assert.HasCount(2, list);
Assert.IsNotType<int[]>(value);

var items = Assert.IsAssignableTo<IEnumerable<int>>(value); // value is IEnumerable<int>
Assert.Contains(2, items);
Assert.IsNotAssignableTo<string>(value);
```
<!-- endref -->

| Assertion | Passes when | Returns |
| --- | --- | --- |
| `IsType<T>(value)` | `value` is exactly of type `T` | `value` as a `T` |
| `IsNotType<T>(value)` | `value` is not exactly of type `T` | |
| `IsAssignableTo<T>(value)` | `value is T`: the type, a derived type or an implemented interface | `value` as a `T` |
| `IsNotAssignableTo<T>(value)` | `value is not T` | |

- A `null` value fails [IsType](cref:Meziantou.Framework.Assertions.Assert.IsType) and [IsAssignableTo](cref:Meziantou.Framework.Assertions.Assert.IsAssignableTo); after them, the compiler knows the value is not `null`.
- A boxed `int` is considered to be exactly of type `int?`, as boxing an `int?` produces a boxed `int`.
- [IsAssignableTo](cref:Meziantou.Framework.Assertions.Assert.IsAssignableTo) matches what `value is T` matches, including COM objects and `IDynamicInterfaceCastable`
  implementations.
- Each assertion has an overload that takes a `Type` instead of a type parameter: `Assert.IsType(typeof(List<int>),
  value)`.
- No value is exactly of an abstract or static type: the analyzer reports [IsType](cref:Meziantou.Framework.Assertions.Assert.IsType) with one as an error (`MFAS0003`).

## Ranges

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertInRangeTests.cs;region=Assert_InRange;format=md-fenced-code-block;dedent=true -->
```csharp
var percentage = 42;

Assert.InRange(percentage, 0, 100);         // the value comes first, then the inclusive bounds
Assert.NotInRange(percentage, 50, 100);

Assert.InRange("banana", "apple", "cherry", StringComparer.Ordinal);
Assert.NotInRange(double.NaN, double.MinValue, double.MaxValue);
```
<!-- endref -->

- `InRange(actual, low, high)` passes when `low <= actual <= high`: both bounds are included. The value under test
  comes **first**, as there is no single expected value.
- The values are compared with `Comparer<T>.Default`, or with the `IComparer<T>` you pass.
- Without a comparer, `NaN` and a `null` `Nullable<T>`, as the value or as a bound, are never in range, like
  `low <= value && value <= high`.
- [NotInRange](cref:Meziantou.Framework.Assertions.Assert.NotInRange) passes when the value is outside the range.
