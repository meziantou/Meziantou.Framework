# Meziantou.Framework.TypeConverter

A powerful and flexible type conversion library for .NET that provides more conversion capabilities than the built-in `System.Convert` class.

## Features

- **Extensive type support**: Converts between primitive types, enums, byte arrays, GUIDs, URIs, CultureInfo, DateTime, TimeSpan, and more
- **Culture-aware conversions**: Supports `IFormatProvider` for culture-specific conversions
- **Flexible byte array formatting**: Convert byte arrays to Base16 (hexadecimal) or Base64 strings
- **Enum parsing**: Parse enums from strings (including flags and comma-separated values)
- **Nullable type support**: Handles nullable types gracefully
- **TypeDescriptor integration**: Falls back to `TypeDescriptor` converters when available
- **Implicit/explicit operators**: Automatically uses implicit and explicit conversion operators
- **Dictionary extensions**: Easily retrieve and convert values from dictionaries
- **Extensible**: Inherit from `DefaultConverter` and override virtual methods for custom conversion logic

## Usage

The types are in the `Meziantou.Framework` namespace.

### Basic Conversions

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

### Byte Array Conversions

<!-- ref:../../tests/Meziantou.Framework.TypeConverter.Tests/DefaultConverterTests_ByteArrayTo.cs;region=TypeConverter_ByteArrayConversions;format=md-fenced-code-block;dedent=true -->
```csharp
var bytes = new byte[] { 1, 2, 3, 4 };

// Default: Base64
var converter1 = new DefaultConverter();
converter1.TryChangeType(bytes, null, out string? base64);
Assert.Equal("AQIDBA==", base64);

// Hexadecimal without prefix
var converter2 = new DefaultConverter
{
    ByteArrayToStringFormat = ByteArrayToStringFormat.Base16,
};
converter2.TryChangeType(bytes, null, out string? hex);
Assert.Equal("01020304", hex);

// Hexadecimal with 0x prefix
var converter3 = new DefaultConverter
{
    ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed,
};
converter3.TryChangeType(bytes, null, out string? hexPrefixed);
Assert.Equal("0x01020304", hexPrefixed);

// Convert from hexadecimal string to byte array
ConvertUtilities.TryChangeType("0x01020304", CultureInfo.InvariantCulture, out byte[]? result);
Assert.Equal([1, 2, 3, 4], result);

// Convert from Base64 string to byte array
ConvertUtilities.TryChangeType("AQIDBA==", CultureInfo.InvariantCulture, out byte[]? result2);
Assert.Equal([1, 2, 3, 4], result2);
```
<!-- endref -->

### Dictionary Extensions

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
