# Meziantou.Framework.FixedStringBuilder.Generator

Source generator and analyzer to create fixed-length string structs.

## Usage

Add a partial struct and decorate it with `FixedStringBuilderAttribute`:

````csharp
[FixedStringBuilderAttribute(10)]
public partial struct FixedStringBuilder10;
````

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
<!-- analyzer-rules -->
