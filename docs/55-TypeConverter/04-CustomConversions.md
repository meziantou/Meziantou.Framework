---
title: Custom conversions
description: Change or add conversions by deriving from DefaultConverter, or implement IConverter.
---

## Derive from DefaultConverter

`DefaultConverter` is not sealed. Its conversions are `protected virtual` methods, so you can change one and keep the
others:

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_CustomConverter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class YesNoConverter : DefaultConverter
{
    protected override bool TryConvert(object? input, Type conversionType, IFormatProvider? provider, out object? value)
    {
        if (input is bool boolean && conversionType == typeof(string))
        {
            value = boolean ? "Yes" : "No";
            return true;
        }

        return base.TryConvert(input, conversionType, provider, out value);
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_CustomConverterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var converter = new YesNoConverter();

Assert.True(converter.TryChangeType(true, CultureInfo.InvariantCulture, out string? text));
Assert.Equal("Yes", text);

// The other conversions are unchanged
Assert.Equal(42, converter.ChangeType("42", defaultValue: 0, CultureInfo.InvariantCulture));
```
<!-- endref -->

- `TryConvert(object? input, Type conversionType, IFormatProvider? provider, out object? value)` runs every conversion.
  Override it to handle a pair of types before the default rules.
- The overloads of `TryConvert` with a typed `out` parameter (`out int`, `out Guid`, `out DateTime`, …) convert to one
  type. Override one to change how that type is read.
- `TryConvertEnum` converts to enums, and `TryConvertUsingImplicitConverter` applies implicit operators.
- `TryChangeType`, the public entry point, is virtual too.

The extension methods of `ConvertUtilities`, such as `TryChangeType<T>` and `ChangeType<T>`, work with any `IConverter`,
so your converter has the same API as `ConvertUtilities`.

## Implement IConverter

`IConverter` has a single method, `TryChangeType(input, conversionType, provider, out value)`. Implement it to write a
converter from scratch, for instance one that delegates to a serializer, and use it with the same extension methods.
