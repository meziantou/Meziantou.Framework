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

### Metadata names

`useMetadata` uses names from:

- `DisplayAttribute.Name`
- `DisplayNameAttribute.DisplayName`
- `EnumMemberAttribute.Value`

When metadata is not available for a member, the declared enum name is used.

# Additional resources

- [Caching Enum.ToString to improve performance](https://www.meziantou.net/caching-enum-tostring-to-improve-performance.htm)
