---
title: Generated members
description: The extension methods and the C# 14 static members generated for an enum, and how they format and parse values.
---

The examples of this page use two enums listed in `FastEnumAttribute`: `Color`, below, and `Permission`, a `[Flags]`
enum with the members `None = 0`, `Read = 1`, `Write = 2` and `Execute = 4`.

<!-- ref:../../tests/Meziantou.Framework.FastEnumGenerator.GeneratorTests/Color.cs;region=FastEnum_MetadataEnum;format=md-fenced-code-block;dedent=true -->
```csharp
public enum Color
{
    [Display(Name = "Blue metadata")]
    Blue,

    [EnumMember(Value = "Red metadata")]
    Red,

    Green,
}
```
<!-- endref -->

## Extension methods

These methods are generated for every enum, whatever the C# version of the project:

| Method | Equivalent | Result |
| --- | --- | --- |
| `value.ToStringFast()` | `value.ToString()` | The name of the member. |
| `value.ToStringFast(bool useMetadata)` | | The [metadata name](#use-metadata-names) of the member when `useMetadata` is `true`. |
| `value.GetName()` | `Enum.GetName(value)` | The name of the member, or `null` when the value is not declared. |
| `value.HasFlagFast(TEnum flag)` | `value.HasFlag(flag)` | `true` when all the bits of `flag` are set: `(value & flag) == flag`. |

<!-- ref:../../tests/Meziantou.Framework.FastEnumGenerator.GeneratorTests/FastEnumGeneratedBehaviorTests.cs;region=FastEnum_InstanceMethods;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Green", Color.Green.ToStringFast());
Assert.Equal("Green", Color.Green.GetName());

// Values that are not declared
Assert.Equal("42", ((Color)42).ToStringFast());
Assert.Null(((Color)42).GetName());

// Flags
var permission = Permission.Read | Permission.Write;
Assert.Equal("Read, Write", permission.ToStringFast());
Assert.True(permission.HasFlagFast(Permission.Write));
Assert.False(permission.HasFlagFast(Permission.Execute));
```
<!-- endref -->

A value that is not a declared member is formatted like `Enum.ToString` formats it: a `[Flags]` value is decomposed
into its members (`Read, Write`), and any other value is formatted as its number. Use `GetName` instead of
`ToStringFast` to detect undeclared values.

`HasFlagFast` is not named `HasFlag` on purpose: the members of `System.Enum` win over extension methods, so a method
named `HasFlag` would never be called. The same applies to `IsDefinedFast` below.

## Static members (C# 14)

When the project uses C# 14 or later, the generator also adds static members to the enum type itself, with C# 14
extension members. You call them like the methods of `System.Enum`, on the enum type:

| Member | Equivalent |
| --- | --- |
| `TEnum.Parse(string value, bool ignoreCase)` | `Enum.Parse<TEnum>(value, ignoreCase)` |
| `TEnum.Parse(ReadOnlySpan<char> value, bool ignoreCase)` | `Enum.Parse<TEnum>(value, ignoreCase)` |
| `TEnum.TryParse(string? value, bool ignoreCase, out TEnum result)` | `Enum.TryParse(value, ignoreCase, out result)` |
| `TEnum.TryParse(ReadOnlySpan<char> value, bool ignoreCase, out TEnum result)` | `Enum.TryParse(value, ignoreCase, out result)` |
| `TEnum.IsDefinedFast(TEnum value)` | `Enum.IsDefined(value)` |
| `TEnum.GetNames(bool useMetadata)` | `Enum.GetNames<TEnum>()` |
| `TEnum.GetValues()` | `Enum.GetValues<TEnum>()` |

`Parse` and `TryParse` also have overloads with a `bool useMetadata` parameter, before the `out` parameter.

<!-- ref:../../tests/Meziantou.Framework.FastEnumGenerator.GeneratorTests/FastEnumGeneratedBehaviorTests.cs;region=FastEnum_StaticMembers;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(Color.Red, Color.Parse("red", ignoreCase: true));
Assert.Equal(Permission.Read | Permission.Write, Permission.Parse("Read, Write", ignoreCase: false));

Assert.True(Color.TryParse("Blue", ignoreCase: false, out var color));
Assert.Equal(Color.Blue, color);
Assert.False(Color.TryParse("Purple", ignoreCase: true, out _));

Assert.True(Color.IsDefinedFast(Color.Green));
Assert.False(Color.IsDefinedFast((Color)42));

Assert.Equal(["Blue", "Red", "Green"], Color.GetNames(useMetadata: false).ToArray());
Assert.Equal([Color.Blue, Color.Red, Color.Green], Color.GetValues().ToArray());
```
<!-- endref -->

- `Parse` and `TryParse` accept what `Enum.Parse` accepts: names, numbers, and comma-separated names for `[Flags]`
  enums. `Parse` throws the same exceptions.
- `GetNames` and `GetValues` order the members by value, like `Enum.GetNames` and `Enum.GetValues`, but return a
  `ReadOnlySpan<T>` instead of a new array. Call `ToArray()` when you need an array.

The members need a target framework with the span-based `Enum.TryParse` overloads (.NET Core 2.1, .NET Standard 2.1
and later). On a project that uses C# 13 or earlier, only the extension methods are generated, and the analyzer does not
suggest the static members.

## Use metadata names

With `useMetadata: true`, the methods use the name given by an attribute on the member instead of its declared name:

- `[Display(Name = "...")]` (`System.ComponentModel.DataAnnotations`)
- `[EnumMember(Value = "...")]` (`System.Runtime.Serialization`)

A member without these attributes keeps its declared name. `Parse` and `TryParse` with `useMetadata: true` read
the metadata names, and `GetNames(useMetadata: true)` returns them:

<!-- ref:../../tests/Meziantou.Framework.FastEnumGenerator.GeneratorTests/FastEnumGeneratedBehaviorTests.cs;region=FastEnum_MetadataNames;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Blue", Color.Blue.ToStringFast());
Assert.Equal("Blue metadata", Color.Blue.ToStringFast(useMetadata: true)); // [Display(Name = ...)]
Assert.Equal("Red metadata", Color.Red.ToStringFast(useMetadata: true)); // [EnumMember(Value = ...)]
Assert.Equal("Green", Color.Green.ToStringFast(useMetadata: true)); // No metadata: the declared name

Assert.Equal(Color.Red, Color.Parse("red metadata", ignoreCase: true, useMetadata: true));
Assert.Equal(["Blue metadata", "Red metadata", "Green"], Color.GetNames(useMetadata: true).ToArray());
```
<!-- endref -->
