---
title: Custom conversions
description: Change or add conversions by deriving from DefaultConverter, or implement IConverter.
---

## Derive from DefaultConverter

[DefaultConverter](cref:Meziantou.Framework.DefaultConverter) is not sealed. Its conversions are `protected virtual` methods, so you can change one and keep the
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
- The overloads of [TryConvert](cref:Meziantou.Framework.DefaultConverter.TryConvert) with a typed `out` parameter (`out int`, `out Guid`, `out DateTime`, …) convert to one
  type. Override one to change how that type is read.
- [TryConvertEnum](cref:Meziantou.Framework.DefaultConverter.TryConvertEnum) converts to enums, and [TryConvertUsingImplicitConverter](cref:Meziantou.Framework.DefaultConverter.TryConvertUsingImplicitConverter) applies implicit operators.
- [TryChangeType](cref:Meziantou.Framework.DefaultConverter.TryChangeType), the public entry point, is virtual too.

The extension methods of [ConvertUtilities](cref:Meziantou.Framework.ConvertUtilities), such as [TryChangeType\<T\>](cref:Meziantou.Framework.ConvertUtilities.TryChangeType) and [ChangeType\<T\>](cref:Meziantou.Framework.ConvertUtilities.ChangeType), work with any [IConverter](cref:Meziantou.Framework.IConverter),
so your converter has the same API as [ConvertUtilities](cref:Meziantou.Framework.ConvertUtilities).

## Implement IConverter

[IConverter](cref:Meziantou.Framework.IConverter) has a single method, `TryChangeType(input, conversionType, provider, out value)`. Implement it to write a
converter from scratch, for instance one that delegates to a serializer, and use it with the same extension methods.
