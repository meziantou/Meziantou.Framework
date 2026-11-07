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

The behavior does not change: `GetNames` and `GetValues` still return a new array that the caller owns, ordered by
value, `GetName` still returns `null` for an undeclared value, and `HasFlag` still throws when the flag is of another
enum type.

## Limitations

- Only the generic overloads, such as `Enum.GetNames<TEnum>()`, are intercepted. The overloads that take a `Type`
  return `object` or `Array`, which an interceptor cannot change.
- `Parse` and `TryParse` are not intercepted: call the [generated members](03-GeneratedMembers.md#static-members-c-14)
  directly.
- `ToString()` and `HasFlag()` are declared on `System.Enum`, so the value is still boxed at the call site, as it is
  without interceptors. The gain is to skip the reflection, not the allocation of the box.
- To find the call sites, the generator inspects every call to `ToString`, `HasFlag`, `IsDefined`, `GetName`,
  `GetNames` and `GetValues` in the project, which costs more in the IDE than the default mode.
