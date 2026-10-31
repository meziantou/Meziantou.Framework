---
title: Getting started
description: Install the package, convert values with ConvertUtilities, and read values from a dictionary.
---

## Install the package

Add the `Meziantou.Framework.TypeConverter` package to your project:

<InstallNuGetPackage Id="Meziantou.Framework.TypeConverter" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;` and
`using System.Globalization;`. They check their results with assertions such as `Assert.Equal`, which show what each
call returns.

## Convert a value

[ConvertUtilities](cref:Meziantou.Framework.ConvertUtilities) has two families of methods:

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
// TryChangeType reports whether the conversion succeeded
Assert.True(ConvertUtilities.TryChangeType("42", CultureInfo.InvariantCulture, out int number));
Assert.Equal(42, number);
Assert.False(ConvertUtilities.TryChangeType("forty-two", CultureInfo.InvariantCulture, out int _));

// ChangeType returns a default value when the conversion fails
Assert.Equal(-1, ConvertUtilities.ChangeType("forty-two", defaultValue: -1, CultureInfo.InvariantCulture));

// The target type can be known only at run time
Assert.True(ConvertUtilities.TryChangeType("3.5", typeof(double), CultureInfo.InvariantCulture, out var value));
Assert.Equal(3.5, value);
```
<!-- endref -->

- [TryChangeType](cref:Meziantou.Framework.ConvertUtilities.TryChangeType) returns `true` and the converted value, or `false` when the value cannot be converted. Use it when
  you need to know whether the conversion succeeded, for instance to report an invalid configuration value.
- [ChangeType](cref:Meziantou.Framework.ConvertUtilities.ChangeType) returns the converted value, or the default value you pass when the conversion fails. Without a default
  value, it returns the default of the type (`0`, `null`, …).
- Both have a generic overload, [TryChangeType\<T\>](cref:Meziantou.Framework.ConvertUtilities.TryChangeType) and [ChangeType\<T\>](cref:Meziantou.Framework.ConvertUtilities.ChangeType), and an overload that takes the target type as a
  `Type`.
- `null` and `DBNull.Value` convert to `null` for reference types and nullable types. For other value types, the
  conversion fails.

## Pass a culture

The methods take an `IFormatProvider`, usually a `CultureInfo`, that is used to parse and format numbers and dates:

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/ConvertUtilitiesTests.cs;region=TypeConverter_BasicConversions;format=md-fenced-code-block;dedent=true -->
```csharp
// Using TryChangeType (recommended)
Assert.True(ConvertUtilities.TryChangeType("42", CultureInfo.InvariantCulture, out int value));
Assert.Equal(42, value);

// Using ChangeType with default value
var result = ConvertUtilities.ChangeType<int>("invalid", defaultValue: 0, provider: CultureInfo.InvariantCulture);
Assert.Equal(0, result);

// Convert with culture-specific formatting
var cultureInfo = CultureInfo.GetCultureInfo("fr-FR");
var number = ConvertUtilities.ChangeType<decimal>("1234,56", defaultValue: 0m, provider: cultureInfo);
Assert.Equal(1234.56m, number);
```
<!-- endref -->

Pass `CultureInfo.InvariantCulture` for text written by programs, such as configuration files, URLs or JSON, and the
user's culture for text typed by a user. The overloads without a provider pass `null`, so the parsing methods use the
current culture: the same text can then give different values on machines with different regional settings.

## Read values from a dictionary

[GetValueOrDefault](cref:Meziantou.Framework.DictionaryExtensions.GetValueOrDefault) and [TryGetValueOrDefault](cref:Meziantou.Framework.DictionaryExtensions.TryGetValueOrDefault) read a value from an `IReadOnlyDictionary<TKey, TValue>` and convert it:

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/DictionaryExtensionsTests.cs;region=TypeConverter_DictionaryExtensions;format=md-fenced-code-block;dedent=true -->
```csharp
var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
{
    ["age"] = "25",
    ["price"] = 19.99,
    ["active"] = true,
};

// Get and convert values with type safety
var age = dictionary.GetValueOrDefault("age", 0);
Assert.Equal(25, age); // as int

var price = dictionary.GetValueOrDefault("price", 0.0);
Assert.Equal(19.99, price); // as double

// Use default value when key doesn't exist
var missing = dictionary.GetValueOrDefault("missing", "default");
Assert.Equal("default", missing);

// TryGetValueOrDefault for better control
Assert.True(dictionary.TryGetValueOrDefault("age", out int ageValue));
Assert.Equal(25, ageValue);
```
<!-- endref -->

The type of the result is the type of the default value, or the type of the `out` variable. The default value is
returned when the key is missing or when the value cannot be converted. These methods convert without a format
provider, so they use the current culture.

## Next steps

- [Supported conversions](03-SupportedConversions.md): what each target type accepts.
- [Custom conversions](04-CustomConversions.md): change a conversion or add one.
