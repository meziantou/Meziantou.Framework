---
title: Underlying types
description: The types an ID can wrap, the name of their factory method, and how each one is formatted, parsed and written to JSON.
---

An ID wraps one value of a supported type. Any other type makes the analyzer report
[MFSTID0001](11-Diagnostics.md#mfstid0001-not-supported-type), and no code is generated.

## Supported types

| Underlying type | Factory method | JSON value (System.Text.Json) |
| --- | --- | --- |
| `bool` | `FromBoolean` | `true` or `false` |
| `byte` | `FromByte` | number |
| `sbyte` | `FromSByte` | number |
| `short` | `FromInt16` | number |
| `ushort` | `FromUInt16` | number |
| `int` | `FromInt32` | number |
| `uint` | `FromUInt32` | number |
| `long` | `FromInt64` | number |
| `ulong` | `FromUInt64` | number |
| `Int128` | `FromInt128` | number, with all its digits |
| `UInt128` | `FromUInt128` | number, with all its digits |
| `System.Numerics.BigInteger` | `FromBigInteger` | number, with all its digits |
| `Half` | `FromHalf` | number |
| `float` | `FromSingle` | number |
| `double` | `FromDouble` | number |
| `decimal` | `FromDecimal` | number |
| `Guid` | `FromGuid` | string |
| `string` | `FromString` | string |
| `DateTime` | `FromDateTime` | string (ISO 8601) |
| `DateTimeOffset` | `FromDateTimeOffset` | string (ISO 8601) |
| `MongoDB.Bson.ObjectId` | `FromObjectId` | string |

`MongoDB.Bson.ObjectId` needs a reference to the MongoDB.Bson package. `Int128`, `UInt128` and `Half` need a target
framework that has them.

## Pick a type

- **`int` or `long`** for the keys of a database table that generates them.
- **`Guid`** for IDs created by the application: the generated `New()` method creates one. See
  [Create Guid values](07-Options.md#create-guid-values).
- **`string`** for codes and external references (SKUs, country codes, user names from an identity provider). Choose
  how they are compared with the [`StringComparison` option](07-Options.md#compare-string-values).

## How values are formatted and parsed

[ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString), `ToString()` and the converters that write strings format the value with the invariant culture:

- Numbers use the invariant culture, without a format: `42`, `1.5`.
- `Guid` uses its default format: `0f8fad5b-d9cb-469f-a165-70867728950e`.
- `bool` is `True` or `False`.
- `DateTime` uses the round-trip format (`"o"`). `DateTimeOffset` is converted to UTC first, so the offset is not in
  [ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString).

`Parse` and `TryParse` read the same representations with the invariant culture:

- Numbers accept any number style (`NumberStyles.Any`): signs, thousands separators, decimal points, exponents and
  surrounding white space.
- `DateTime` and `DateTimeOffset` are adjusted to UTC (`DateTimeStyles.AdjustToUniversal`).
- `bool` and `Guid` use `bool.TryParse` and `Guid.TryParse`.
- A `string` ID accepts any string, but not `null`.

`ObjectId` IDs have no `Parse(ReadOnlySpan<char>)` overload and do not implement `ISpanParsable<T>`, because
`ObjectId` has no such method.
