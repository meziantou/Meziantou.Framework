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

The generated extension class contains:
- `ToStringFast(this TEnum value)`
- `ToStringFast(this TEnum value, bool useMetadata)`
- `HasFlag(this TEnum value, TEnum flag)`
- `GetName(this TEnum value)`

When the target project supports C# 14 extension members, the generator also adds:
- `Parse` / `TryParse` overloads (`string` and `ReadOnlySpan<char>`)
- `IsDefined`
- `GetNames(bool useMetadata)`
- `GetValues()`

`useMetadata` uses names from `DisplayAttribute` and `EnumMemberAttribute` when available.

# Additional resources

- [Caching Enum.ToString to improve performance](https://www.meziantou.net/caching-enum-tostring-to-improve-performance.htm)
