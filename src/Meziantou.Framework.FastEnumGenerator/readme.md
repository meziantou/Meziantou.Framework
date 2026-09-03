# Meziantou.Framework.FastEnumGenerator

The source generator generates specialized enum helpers for selected enum types.

````csharp
[assembly: FastEnumAttribute(typeof(Sample.Color), IsPublic = true, ExtensionMethodNamespace = "Sample.Extensions")]

namespace Sample
{
    public enum Color
    {
        Blue,
        Red,
        Green,
    }
}
````

The generated class is emitted in the enum's namespace, or in `ExtensionMethodNamespace` when that is set.
Call sites must have that namespace in scope:

````csharp
using Sample.Extensions; // only needed when ExtensionMethodNamespace is used

var name = Color.Blue.ToStringFast();
````

## Generated methods

For each configured enum, the generator emits these instance extension methods:

- `string ToStringFast(this TEnum value)`
- `string ToStringFast(this TEnum value, bool useMetadata)`
- `bool HasFlagFast(this TEnum instance, TEnum flag)`
- `string? GetName(this TEnum instance)`

Method behavior:

- `ToStringFast` returns the declared enum name without using reflection. Values that are not a declared
  member are formatted the way `Enum.ToString` formats them: `[Flags]` enums are decomposed into their
  members, and anything else is formatted as its underlying number.
- `ToStringFast(..., useMetadata: true)` uses metadata names when available.
- `GetName` matches `Enum.GetName`: it returns `null` when the value is not a declared member. Use
  `ToStringFast` if you want the numeric fallback instead.
- `HasFlagFast` uses typed bitwise operations (`(instance & flag) == flag`).

> `HasFlagFast` and `IsDefinedFast` are deliberately not called `HasFlag` and `IsDefined`. Members
> inherited from `System.Enum` win overload resolution over extension members, so methods with those
> names would never be called.

When the target project supports C# 14 extension members, the generator also emits static members on `extension(TEnum)`:

- `TEnum Parse(string value, bool ignoreCase)`
- `TEnum Parse(ReadOnlySpan<char> value, bool ignoreCase)`
- `TEnum Parse(string value, bool ignoreCase, bool useMetadata)`
- `TEnum Parse(ReadOnlySpan<char> value, bool ignoreCase, bool useMetadata)`
- `bool TryParse(string? value, bool ignoreCase, out TEnum result)`
- `bool TryParse(ReadOnlySpan<char> value, bool ignoreCase, out TEnum result)`
- `bool TryParse(string? value, bool ignoreCase, bool useMetadata, out TEnum result)`
- `bool TryParse(ReadOnlySpan<char> value, bool ignoreCase, bool useMetadata, out TEnum result)`
- `bool IsDefinedFast(TEnum value)`
- `ReadOnlySpan<string> GetNames(bool useMetadata)`
- `ReadOnlySpan<TEnum> GetValues()`

These members use `ReadOnlySpan<char>` and require a target framework where the span-based
`Enum.TryParse` overloads exist (.NET Core 2.1 / .NET Standard 2.1 and later). On a project targeting
C# 13 or earlier, only the instance extension methods above are generated.

`GetNames` and `GetValues` order their results by the underlying value, like `Enum.GetNames` and
`Enum.GetValues`, so they can be substituted for those methods. Unlike them, they return a
`ReadOnlySpan<T>` rather than an array.

## Analyzer rules

The package also ships analyzers and code fixes for enums configured with `FastEnumAttribute`.

The rules suggesting `Parse`, `TryParse`, `GetNames`, `GetValues` and `IsDefinedFast` are only reported
when the project supports C# 14 extension members, because those members are not generated otherwise.

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFEG0001` | FastEnumGenerator | FastEnum target type is invalid | Error | ✔️ |
| `MFEG0002` | FastEnumGenerator | Use FastEnum Parse | Warning | ✔️ |
| `MFEG0003` | FastEnumGenerator | Use FastEnum TryParse | Warning | ✔️ |
| `MFEG0004` | FastEnumGenerator | Use FastEnum GetNames | Warning | ✔️ |
| `MFEG0005` | FastEnumGenerator | Use FastEnum GetValues | Warning | ✔️ |
| `MFEG0006` | FastEnumGenerator | Use FastEnum GetName | Warning | ✔️ |
| `MFEG0007` | FastEnumGenerator | Use FastEnum IsDefinedFast | Warning | ✔️ |
| `MFEG0008` | FastEnumGenerator | Use FastEnum ToStringFast | Warning | ✔️ |
| `MFEG0009` | FastEnumGenerator | FastEnum target enum has no members | Warning | ✔️ |
<!-- analyzer-rules -->

### Metadata names

`useMetadata` uses names from:

- `DisplayAttribute.Name`
- `DisplayNameAttribute.DisplayName`
- `EnumMemberAttribute.Value`

When metadata is not available for a member, the declared enum name is used.

# Additional resources

- [Caching Enum.ToString to improve performance](https://www.meziantou.net/caching-enum-tostring-to-improve-performance.htm)
