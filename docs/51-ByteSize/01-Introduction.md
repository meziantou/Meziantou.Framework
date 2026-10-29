---
title: Introduction
description: What the ByteSize struct does, and the decimal and binary units it supports.
---

`Meziantou.Framework.ByteSize` provides `ByteSize`, a struct that holds a number of bytes, such as the size of a file, a
download or a memory limit. It turns `1536000` into `1.54MB` or `1.46MiB` for display, reads `10MB` from a
configuration file, and compares and adds sizes.

## Decimal and binary units

`ByteSize` supports two families of units:

| Decimal units | Value | Binary units | Value |
| --- | --- | --- | --- |
| `kB` (kilobyte) | 1,000 bytes | `kiB` (kibibyte) | 1,024 bytes |
| `MB` (megabyte) | 1,000² bytes | `MiB` (mebibyte) | 1,024² bytes |
| `GB` (gigabyte) | 1,000³ bytes | `GiB` (gibibyte) | 1,024³ bytes |
| `TB` (terabyte) | 1,000⁴ bytes | `TiB` (tebibyte) | 1,024⁴ bytes |
| `PB` (petabyte) | 1,000⁵ bytes | `PiB` (pebibyte) | 1,024⁵ bytes |
| `EB` (exabyte) | 1,000⁶ bytes | `EiB` (exbibyte) | 1,024⁶ bytes |

`B` is a byte. The `ByteSizeUnit` enum has one member per unit, from `Byte` to `ExbiByte`, and its value is the number
of bytes of the unit.

## What you can do with it

- **Create sizes** from a number of bytes or from a value in any unit. See [Getting started](02-GettingStarted.md).
- **Format sizes** with the best unit or a given unit, and a number of decimals. See
  [Formatting and parsing](03-FormattingAndParsing.md).
- **Parse sizes** from strings, spans and UTF-8 bytes. See [Formatting and parsing](03-FormattingAndParsing.md#parse-a-size).
- **Compare and compute** sizes with the usual operators. See [Getting started](02-GettingStarted.md#compare-and-compute).

## Namespaces

`ByteSize` and `ByteSizeUnit` are in the `Meziantou.Framework` namespace.
