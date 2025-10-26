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

### Basic Conversions

```csharp
using Meziantou.Framework;

// Using TryChangeType (recommended)
if (ConvertUtilities.TryChangeType("42", out int value))
{
    Console.WriteLine(value); // 42
}

// Using ChangeType with default value
var result = ConvertUtilities.ChangeType<int>("invalid", defaultValue: 0);
Console.WriteLine(result); // 0

// Convert with culture-specific formatting
var cultureInfo = CultureInfo.GetCultureInfo("fr-FR");
var number = ConvertUtilities.ChangeType<decimal>("1234,56", provider: cultureInfo);
Console.WriteLine(number); // 1234.56
```

### Byte Array Conversions

```csharp
var bytes = new byte[] { 1, 2, 3, 4 };

// Default: Base64
var converter1 = new DefaultConverter();
converter1.TryChangeType(bytes, null, out string base64);
Console.WriteLine(base64); // "AQIDBA=="

// Hexadecimal without prefix
var converter2 = new DefaultConverter
{
    ByteArrayToStringFormat = ByteArrayToStringFormat.Base16
};
converter2.TryChangeType(bytes, null, out string hex);
Console.WriteLine(hex); // "01020304"

// Hexadecimal with 0x prefix
var converter3 = new DefaultConverter
{
    ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed
};
converter3.TryChangeType(bytes, null, out string hexPrefixed);
Console.WriteLine(hexPrefixed); // "0x01020304"

// Convert from hexadecimal string to byte array
ConvertUtilities.TryChangeType("0x01020304", out byte[] result);
// result = [1, 2, 3, 4]

// Convert from Base64 string to byte array
ConvertUtilities.TryChangeType("AQIDBA==", out byte[] result2);
// result2 = [1, 2, 3, 4]
```

### Dictionary Extensions

```csharp
var dictionary = new Dictionary<string, object>
{
    ["age"] = "25",
    ["price"] = 19.99,
    ["active"] = true
};

// Get and convert values with type safety
var age = dictionary.GetValueOrDefault("age", 0);
Console.WriteLine(age); // 25 (as int)

var price = dictionary.GetValueOrDefault("price", 0.0);
Console.WriteLine(price); // 19.99 (as double)

// Use default value when key doesn't exist
var missing = dictionary.GetValueOrDefault("missing", "default");
Console.WriteLine(missing); // "default"

// TryGetValueOrDefault for better control
if (dictionary.TryGetValueOrDefault("age", out int ageValue))
{
    Console.WriteLine($"Age: {ageValue}");
}
```
