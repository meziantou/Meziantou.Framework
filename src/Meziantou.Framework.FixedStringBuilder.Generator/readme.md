# Meziantou.Framework.FixedStringBuilder.Generator

Source generator and analyzer to create fixed-length string structs.

## Usage

Add a partial struct and decorate it with `FixedStringBuilderAttribute`:

<!-- ref:../../tests/Meziantou.Framework.FixedStringBuilder.Tests/FixedStringBuilderTests.cs;region=FixedStringBuilderGenerator_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
[FixedStringBuilderAttribute(10)]
public partial struct FixedStringBuilder10;
```
<!-- endref -->

The generator emits:

- `Microsoft.CodeAnalysis.EmbeddedAttribute`
- `FixedStringBuilderAttribute` (internal partial class)
- members for the target struct (constructors, interpolation support, `TryFormat`, equality, etc.)

Generated fixed strings enforce capacity and throw `ArgumentException` when data does not fit.

## Analyzer diagnostics

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFFSG0001` | FixedStringBuilderGenerator | FixedStringBuilderAttribute requires one argument | Error | ✔️ |
| `MFFSG0002` | FixedStringBuilderGenerator | FixedStringBuilderAttribute argument type is invalid | Error | ✔️ |
| `MFFSG0003` | FixedStringBuilderGenerator | FixedStringBuilderAttribute length must be positive | Error | ✔️ |
| `MFFSG0004` | FixedStringBuilderGenerator | FixedStringBuilderAttribute length is too large | Error | ✔️ |
| `MFFSG0005` | FixedStringBuilderGenerator | FixedStringBuilderAttribute requires a partial struct | Error | ✔️ |
| `MFFSG0006` | FixedStringBuilderGenerator | FixedStringBuilderAttribute does not support readonly or ref structs | Error | ✔️ |
<!-- analyzer-rules -->
