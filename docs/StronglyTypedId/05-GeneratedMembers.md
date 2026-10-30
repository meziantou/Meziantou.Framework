---
title: Generated members
description: The members and interfaces the generator adds to an ID, and how parsing, formatting and equality behave.
---

For `[StronglyTypedId<int>] public partial struct ProjectId { }`, the generator adds the members below. The names that
depend on the underlying type, such as `FromInt32`, follow the table of [underlying types](04-UnderlyingTypes.md).

## Members

| Member | Description |
| --- | --- |
| `Value` | The underlying value. |
| `ValueAsString` | The value formatted with the invariant culture. See [how values are formatted](04-UnderlyingTypes.md#how-values-are-formatted-and-parsed). |
| `FromInt32(int value)` | Creates an ID. |
| `New()` | `Guid` IDs only: creates an ID from a new `Guid`. See [Create Guid values](07-Options.md#create-guid-values). |
| Constructor `ProjectId(int value)` | `private`, or `protected` in a class that is not `sealed`. Write your own to [validate the value](06-ValidationAndCustomMembers.md). |
| `ToString()` | `ProjectId { Value = 42 }`, or `42` with [`GenerateToStringAsRecord = false`](07-Options.md#format-tostring-as-the-value). |
| `Equals(ProjectId)`, `Equals(object?)`, `GetHashCode()` | Equality of the values (`IEquatable<ProjectId>`). |
| `==`, `!=` | The same equality, as operators. |
| `Parse(string)`, `Parse(ReadOnlySpan<char>)` | Parse the string representation. Throw a `FormatException` when the value is not valid. |
| `TryParse(string?, out ProjectId)`, `TryParse(ReadOnlySpan<char>, out ProjectId)` | Return `false` when the value is not valid, `null` or rejected by your constructor. |
| `CompareTo`, `<`, `<=`, `>`, `>=` | Only when the type declares `IComparable` or `IComparable<T>`. See [Implement IComparable](03-DeclaringIds.md#implement-icomparable-to-sort-ids). |

The generator also adds a private `_value` field that stores the value, and private nested classes for the converters:
`ProjectIdTypeConverter`, `ProjectIdJsonConverter`, `ProjectIdNewtonsoftJsonConverter` and `ProjectIdBsonConverter`.
Attributes on the type register them, so you never use them directly. See [Integrations](09-Integrations/01-Overview.md).

## Interfaces

| Interface | Implemented when |
| --- | --- |
| `IEquatable<T>` | Always. |
| `IFormattable` | Always. |
| `ISpanFormattable` | The target framework has it (.NET 6 or later). |
| `IParsable<T>`, `ISpanParsable<T>` | The target framework has them (.NET 7 or later) and the project uses C# 11 or later. |
| `IUtf8SpanFormattable`, `IUtf8SpanParsable<T>` | The target framework has them (.NET 8 or later); `IUtf8SpanParsable<T>` also needs C# 11. |
| `IStronglyTypedId`, `IStronglyTypedId<TValue>` | The project references the [interfaces package](10-Interfaces.md). |
| `IComparable`, `IComparable<T>` | The type declares one of them. |

The members of `IParsable<T>`, `ISpanParsable<T>`, `IUtf8SpanParsable<T>`, `IFormattable`, `ISpanFormattable` and
`IUtf8SpanFormattable` are implemented explicitly: the type has no public overload that takes an `IFormatProvider`,
because the provider would be ignored. Generic code that uses these interfaces, and string interpolation, still call
them.

## Parsing

Parsing always uses the invariant culture, whatever the current culture or the `IFormatProvider` given to the
interface members. The UTF-8 members of `IUtf8SpanParsable<T>` decode the text and parse it like a string, so both
representations accept the same values, and invalid UTF-8 is rejected.

## Formatting

`IFormattable`, `ISpanFormattable` and `IUtf8SpanFormattable` follow two rules:

- Without a format, the result is the same as `ToString()`: `$"{id}"` and `id.ToString()` return the same text.
- With a format, the format applies to `Value`, with the invariant culture. For instance, `$"{id:N}"` formats a `Guid`
  without hyphens. The format is ignored when the underlying type has none, such as `string`, `bool` and `ObjectId`.

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Format;format=md-fenced-code-block;dedent=true -->
```csharp
var id = ProjectId.FromInt32(42);
Assert.Equal("ProjectId { Value = 42 }", $"{id}");
Assert.Equal("00042", $"{id:D5}");
```
<!-- endref -->

## Equality

Two IDs are equal when their values are equal. The values are compared with the equality of the underlying type, and
`string` values with an ordinal comparison unless you choose another one with the
[`StringComparison` option](07-Options.md#compare-string-values). Two IDs of different types are never equal, even with the
same value: `Equals(object?)` checks the type first.
