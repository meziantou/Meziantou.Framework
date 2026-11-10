---
title: Interceptors
description: Make the existing System.Enum calls run the generated code, without changing the call sites.
---

The generated methods are only used where your code calls them. Interceptors are another way: the compiler replaces
the `System.Enum` calls on the listed enums by calls to the generated code, so existing code gets it without any
change. They are disabled by default, because they change what a line of code does without changing how it looks.

## Enable interceptors

Set `MeziantouFastEnumInterceptors` in the project file:

```xml
<PropertyGroup>
  <MeziantouFastEnumInterceptors>true</MeziantouFastEnumInterceptors>
</PropertyGroup>
```

Interceptors require C# 12 or later: with an older version, nothing is intercepted. The package adds the namespace of
the generated interceptors to the `InterceptorsNamespaces` property for you.

The code stays the same, and its results too:

<!-- ref:../../tests/Meziantou.Framework.FastEnumGenerator.InterceptorTests/FastEnumInterceptorTests.cs;region=FastEnum_InterceptedCalls;format=md-fenced-code-block;dedent=true -->
```csharp
// Plain System.Enum calls: with interceptors enabled, they run the generated code
Assert.Equal("Bravo", InterceptedColor.Bravo.ToString());
Assert.Equal("Alpha", Enum.GetName(InterceptedColor.Alpha));
Assert.True(Enum.IsDefined(InterceptedColor.Charlie));
Assert.Equal(["Alpha", "Bravo", "Charlie"], Enum.GetNames<InterceptedColor>());
Assert.Equal(InterceptedColor.Charlie, Enum.Parse<InterceptedColor>("charlie", ignoreCase: true));
Assert.True(Enum.TryParse("Alpha", ignoreCase: false, out InterceptedColor color));
Assert.Equal(InterceptedColor.Alpha, color);
```
<!-- endref -->

## Intercepted calls

These calls are intercepted when the enum is listed in a `FastEnumAttribute`:

| Call | Replaced by |
| --- | --- |
| `value.ToString()` | `ToStringFast` |
| `value.HasFlag(flag)` | `HasFlagFast` |
| `Enum.IsDefined<TEnum>(value)` | The generated `IsDefinedFast` |
| `Enum.GetName<TEnum>(value)` | The generated `GetName` |
| `Enum.GetNames<TEnum>()` | The generated table of names |
| `Enum.GetValues<TEnum>()` | The generated table of values |
| `Enum.Parse<TEnum>(value)` and `Enum.Parse<TEnum>(value, ignoreCase)` | The generated parser |
| `Enum.TryParse<TEnum>(value, out result)` and `Enum.TryParse<TEnum>(value, ignoreCase, out result)` | The generated parser |

`Parse` and `TryParse` are intercepted for a `string` and for a `ReadOnlySpan<char>` value.

The behavior does not change: `GetNames` and `GetValues` still return a new array that the caller owns, ordered by
value, `GetName` still returns `null` for an undeclared value, `HasFlag` still throws when the flag is of another
enum type, and `Parse` still throws the same exception, with the same message, for an invalid value. `Parse` and
`TryParse` read the declared names, not the [metadata names](03-GeneratedMembers.md#use-metadata-names).

## Limitations

- Only the generic overloads, such as `Enum.GetNames<TEnum>()`, are intercepted. The overloads that take a `Type`
  return `object` or `Array`, which an interceptor cannot change.
- `Parse` and `TryParse` are intercepted only on the target frameworks with the span-based `Enum.TryParse` overloads
  (.NET Core 2.1, .NET Standard 2.1 and later), which the generated parser calls for numeric values.
- `ToString()` and `HasFlag()` are declared on `System.Enum`, so the value is still boxed at the call site, as it is
  without interceptors. The gain is to skip the reflection, not the allocation of the box.
- To find the call sites, the generator inspects every call to `ToString`, `HasFlag`, `IsDefined`, `GetName`,
  `GetNames`, `GetValues`, `Parse` and `TryParse` in the project, which costs more in the IDE than the default mode.
