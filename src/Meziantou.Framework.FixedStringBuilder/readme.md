# Meziantou.Framework.FixedStringBuilder

`Meziantou.Framework.FixedStringBuilder` provides fixed-capacity string value types for low-allocation text operations.

## Built-in fixed-capacity types

The package includes the following types:

- `FixedStringBuilder8`
- `FixedStringBuilder16`
- `FixedStringBuilder32`
- `FixedStringBuilder64`

````csharp
using Meziantou.Framework.FixedStringBuilder;

FixedStringBuilder16 sb = $"Hello {"World"}";
sb.AppendLiteral("!");

string str = sb.ToString();
````

If a value does not fit in the configured capacity, operations throw `ArgumentException`.

`Clear()` only resets the length: the characters stay in the underlying buffer. Use `Clear(zeroBuffer: true)` to also
overwrite the whole buffer with `'\0'`.

````csharp
FixedStringBuilder16 sb = $"secret";
sb.Clear(zeroBuffer: true);
````

## Create a custom fixed-capacity type

The package `Meziantou.Framework.FixedStringBuilder.Generator` includes a source generator. Declare a partial struct with `FixedStringBuilderAttribute`:

````csharp
using Meziantou.Framework.FixedStringBuilder;

[FixedStringBuilderAttribute(128)]
public partial struct FixedStringBuilder128;
````

The generated struct supports the same APIs as the built-in types (`Length`, `Clear`, `TryFormat`, interpolation, equality, and conversions from `string`).
