---
title: Appending text
description: Append characters, strings, spans, Unicode scalar values, formatted values and interpolated strings.
---

<!-- ref:../../tests/Meziantou.Framework.ValueStringBuilder.Tests/ValueStringBuilderTests.cs;region=ValueStringBuilder_AppendValues;format=md-fenced-code-block;dedent=true -->
```csharp
using var sb = new ValueStringBuilder(stackalloc char[64]);

sb.Append('[');
sb.Append('=', 3);                      // a character repeated
sb.Append("] ".AsSpan());               // a span of characters
sb.Append(new Rune(0x1F600));           // a Unicode scalar value, here a surrogate pair
sb.Append(' ');
sb.AppendSpanFormattable(42, "D5", CultureInfo.InvariantCulture);

// Interpolated strings are formatted into the builder; pass a format provider to choose the culture
sb.Append(CultureInfo.InvariantCulture, $" {1.5:F2} {DateOnly.MinValue:yyyy}");

Assert.Equal("[===] \U0001F600 00042 1.50 0001", sb.ToString());
```
<!-- endref -->

| Method | Appends |
| --- | --- |
| `Append(char)` | A character |
| `Append(char, count)` | A character repeated `count` times |
| `Append(string?)` | A string; `null` appends nothing |
| `Append(ReadOnlySpan<char>)` | The characters of a span |
| `Append(Rune)` | A Unicode scalar value: one character, or a surrogate pair |
| `AppendSpanFormattable(value, format, provider)` | A value that implements `ISpanFormattable`, such as a number, a date or a `Guid`, formatted directly into the buffer |
| `Append($"...")` | An interpolated string, formatted directly into the builder |
| `Append(provider, $"...")` | An interpolated string, formatted with the culture or the format provider you pass |

## Formatted values

[AppendSpanFormattable](cref:Meziantou.Framework.ValueStringBuilder.AppendSpanFormattable) formats the value straight into the buffer, without creating an intermediate string. When the
value does not fit in the remaining space, it falls back to `ToString(format, provider)`. `format` and `provider` are
optional: `null` uses the default format and the current culture.

## Interpolated strings

`Append($"...")` takes an interpolated string handler: the compiler calls it for each literal part and each value, and
values are formatted without creating the intermediate string that `$"..."` creates elsewhere. Format strings and
alignments work as usual: `{value:F2}`, `{name,-10}`.

The values use the current culture. For text read by programs (file formats, protocols, identifiers), pass
`CultureInfo.InvariantCulture` with `Append(provider, $"...")`, as in the example. A provider that supplies an
`ICustomFormatter` is used too.

The handler formats into a buffer of its own, then appends the result in one step: when formatting a value throws, the
builder is left unchanged.
