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

## Generated methods

For each configured enum, the generator emits these instance extension methods:

- `string ToStringFast(this TEnum value)`
- `string ToStringFast(this TEnum value, bool useMetadata)`
- `bool HasFlag(this TEnum instance, TEnum flag)`
- `string GetName(this TEnum instance)`

Method behavior:

- `ToStringFast` returns the declared enum name without using reflection.
- `ToStringFast(..., useMetadata: true)` uses metadata names when available.
- `GetName` is the non-metadata version of `ToStringFast`.
- `HasFlag` uses typed bitwise operations (`(instance & flag) == flag`).

When the target project supports C# 14 extension members, the generator also emits static members on `extension(TEnum)`:

- `TEnum Parse(string value, bool ignoreCase)`
- `TEnum Parse(ReadOnlySpan<char> value, bool ignoreCase)`
- `TEnum Parse(string value, bool ignoreCase, bool useMetadata)`
- `TEnum Parse(ReadOnlySpan<char> value, bool ignoreCase, bool useMetadata)`
- `bool TryParse(string value, bool ignoreCase, out TEnum result)`
- `bool TryParse(ReadOnlySpan<char> value, bool ignoreCase, out TEnum result)`
- `bool TryParse(string value, bool ignoreCase, bool useMetadata, out TEnum result)`
- `bool TryParse(ReadOnlySpan<char> value, bool ignoreCase, bool useMetadata, out TEnum result)`
- `bool IsDefined(TEnum value)`
- `ReadOnlySpan<string> GetNames(bool useMetadata)`
- `ReadOnlySpan<TEnum> GetValues()`

## Analyzer rules

The package also ships analyzers and code fixes for enums configured with `FastEnumAttribute`.

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFEG0001` | FastEnumGenerator | FastEnum target type is invalid | Error | ✔️ |
| `MFEG0002` | FastEnumGenerator | Use FastEnum Parse | Warning | ✔️ |
| `MFEG0003` | FastEnumGenerator | Use FastEnum TryParse | Warning | ✔️ |
| `MFEG0004` | FastEnumGenerator | Use FastEnum GetNames | Warning | ✔️ |
| `MFEG0005` | FastEnumGenerator | Use FastEnum GetValues | Warning | ✔️ |
| `MFEG0006` | FastEnumGenerator | Use FastEnum GetName | Warning | ✔️ |
| `MFEG0007` | FastEnumGenerator | Use FastEnum IsDefined | Warning | ✔️ |
| `MFEG0008` | FastEnumGenerator | Use FastEnum ToStringFast | Warning | ✔️ |
<!-- analyzer-rules -->

### Metadata names

`useMetadata` uses names from:

- `DisplayAttribute.Name`
- `DisplayNameAttribute.DisplayName`
- `EnumMemberAttribute.Value`

When metadata is not available for a member, the declared enum name is used.

# Additional resources

- [Caching Enum.ToString to improve performance](https://www.meziantou.net/caching-enum-tostring-to-improve-performance.htm)
