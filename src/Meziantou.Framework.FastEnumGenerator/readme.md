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

## Interceptors (opt-in)

Instead of rewriting your call sites, the generator can intercept the `System.Enum` calls in place, so
existing code gets the generated implementation with no source change. This is off by default because it
changes what a call does without changing what it looks like:

````xml
<PropertyGroup>
  <MeziantouFastEnumInterceptors>true</MeziantouFastEnumInterceptors>
</PropertyGroup>
````

It requires C# 12 or later (nothing is generated below that). The package's `.targets` adds the generated
namespace to `InterceptorsNamespaces` for you.

These calls are intercepted, for enums marked with `FastEnumAttribute`:

| Call | Replaced by |
| -- | -- |
| `value.ToString()` | `ToStringFast(value)` |
| `value.HasFlag(flag)` | `HasFlagFast(value, flag)` |
| `Enum.IsDefined<TEnum>(value)` | the generated `IsDefinedFast` |
| `Enum.GetName<TEnum>(value)` | the generated `GetName` |
| `Enum.GetNames<TEnum>()` | the generated names table |
| `Enum.GetValues<TEnum>()` | the generated values table |

Behavior is unchanged: `GetNames`/`GetValues` still return a fresh array the caller owns and still order
by the underlying value, `GetName` still returns `null` for an undefined value, and `HasFlag` still throws
for a flag of a different enum type.

Notes and limitations:

- Only the generic `Enum.X<TEnum>(...)` overloads are intercepted. The `Type`-based overloads return
  `object`/`Array`, which an interceptor cannot change.
- `Parse` and `TryParse` are not intercepted; use the generated members directly.
- `ToString()` and `HasFlag()` are declared on `System.Enum`, so their interceptors must take a
  `System.Enum` receiver and the value is boxed at the call site, exactly as it is today when calling
  `Enum.ToString()`. The win is skipping the reflection inside, not avoiding the box.
- Enabling this makes the generator inspect every `ToString`/`HasFlag`/`IsDefined`/`GetName`/`GetNames`/
  `GetValues` invocation in the project to find call sites, which costs more at design time than the
  default mode.

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
