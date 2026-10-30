---
title: Introduction
description: What the type converter does, and which conversions it supports.
---

`Meziantou.Framework.TypeConverter` converts a value to another type when the target type is only known at run time,
or when the value comes from text: configuration values, query strings, CSV columns, database values, command-line
arguments. `"42"` becomes an `int`, `"yes"` a `bool`, `"friday"` a `DayOfWeek`, and `"0x01020304"` a `byte[]`.

## What you can do with it

- **Convert a value** and get a default value or `false` when it cannot be converted. See
  [Getting started](02-GettingStarted.md).
- **Convert many types**: numbers, booleans, enums, nullable types, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`,
  `CultureInfo`, byte arrays, types with a `TypeConverter`, and types with an implicit conversion operator. See
  [Supported conversions](03-SupportedConversions.md).
- **Read values from a dictionary** and convert them. See
  [Getting started](02-GettingStarted.md#read-values-from-a-dictionary).
- **Change a conversion** by deriving from `DefaultConverter`, or write your own `IConverter`. See
  [Custom conversions](04-CustomConversions.md).

## Namespaces

`ConvertUtilities`, `DefaultConverter`, `IConverter`, `ByteArrayToStringFormat` and `DictionaryExtensions` are in the
[Meziantou.Framework](cref:Meziantou.Framework.TypeConverter!N:Meziantou.Framework) namespace.
