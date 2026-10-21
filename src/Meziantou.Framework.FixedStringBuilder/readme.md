# Meziantou.Framework.FixedStringBuilder

`Meziantou.Framework.FixedStringBuilder` provides fixed-capacity string value types for low-allocation text operations.

## Built-in fixed-capacity types

The package includes the following types:

- `FixedStringBuilder8`
- `FixedStringBuilder16`
- `FixedStringBuilder32`
- `FixedStringBuilder64`

They are in the `Meziantou.Framework.FixedStringBuilder` namespace.

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_Interpolation;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 sb = $"Hello {"World"}";
sb.AppendLiteral("!");

string str = sb.ToString();
Assert.Equal("Hello World!", str);
```
<!-- endref -->

If a value does not fit in the configured capacity, operations throw `ArgumentException`.

`Clear()` only resets the length: the characters stay in the underlying buffer. Use `Clear(zeroBuffer: true)` to also
overwrite the whole buffer with `'\0'`.

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_ClearZeroBuffer;format=md-fenced-code-block;dedent=true -->
```csharp
FixedStringBuilder16 sb = $"secret";
sb.Clear(zeroBuffer: true);
Assert.Equal(0, sb.Length);
```
<!-- endref -->

## Create a custom fixed-capacity type

The package `Meziantou.Framework.FixedStringBuilder.Generator` includes a source generator. Declare a partial struct with `FixedStringBuilderAttribute`:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilder_CustomType;format=md-fenced-code-block;dedent=true -->
```csharp
[FixedStringBuilderAttribute(128)]
public partial struct FixedStringBuilder128;
```
<!-- endref -->

The generated struct supports the same APIs as the built-in types (`Length`, `Clear`, `TryFormat`, interpolation, equality, and conversions from `string`).
