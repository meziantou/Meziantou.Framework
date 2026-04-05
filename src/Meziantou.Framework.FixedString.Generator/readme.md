# Meziantou.Framework.FixedString.Generator

Source generator and analyzer to create fixed-length string structs.

## Usage

Add a partial struct and decorate it with `FixedStringAttribute`:

````csharp
[FixedStringAttribute(10)]
public partial struct FixedString10;
````

The generator emits:

- `Microsoft.CodeAnalysis.EmbeddedAttribute`
- `FixedStringAttribute` (internal partial class)
- members for the target struct (constructors, interpolation support, `TryFormat`, equality, etc.)

Generated fixed strings enforce capacity and throw `ArgumentException` when data does not fit.

## Analyzer diagnostics

- `MFFSG0001`: `FixedStringAttribute` must have exactly one constructor argument.
- `MFFSG0002`: the constructor argument must be an `int` constant.
- `MFFSG0003`: the length must be greater than `0`.
