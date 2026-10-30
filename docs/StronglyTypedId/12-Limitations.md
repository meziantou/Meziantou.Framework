---
title: Limitations
description: What the generator does not do, and the behaviors to know before you rely on it.
---

## Declarations

- **Only the [supported underlying types](04-UnderlyingTypes.md#supported-types)** can be wrapped. An ID wraps a single
  value: there are no composite IDs.
- **No generic IDs**, and no IDs nested in a generic type
  ([MFSTID0004](11-Diagnostics.md#mfstid0004-generic-types-are-not-supported)).
- **The type and its containing types must be `partial`.**
- **The generic attribute needs .NET 10 or later.** On older targets, use `[StronglyTypedId(typeof(int))]`. See
  [Declaring IDs](03-DeclaringIds.md#use-typeof-on-older-targets).

## Behavior

- **The default value of a struct ID is not validated.** `default(ProjectId)` wraps `0` without calling your
  constructor. See [Validate the value](06-ValidationAndCustomMembers.md#validate-the-value).
- **Records keep their own equality and `ToString`.** The `StringComparison` and `GenerateToStringAsRecord` options
  have no effect on a record or a record struct, whose `Equals`, `GetHashCode` and `ToString` are written by the
  compiler. Use a struct or a class when you need these options.
- **`StringComparison` applies to equality only.** [Sorting](03-DeclaringIds.md#implement-icomparable-to-sort-ids)
  `string` IDs uses `Comparer<string>.Default`, which depends on the current culture.
- **The `IFormatProvider` is ignored.** Parsing and formatting always use the invariant culture.

## Integrations

- **System.Text.Json source generation ignores the generated converter.** With a `JsonSerializerContext`, which
  Native AOT applications use, an ID is serialized as `{}`. See
  [System.Text.Json](09-Integrations/02-SystemTextJson.md#source-generation-and-native-aot).
- **No Entity Framework Core converter is generated.** Write one value converter per ID type. See
  [Entity Framework Core](09-Integrations/07-EntityFrameworkCore.md).
- **The converters are private.** They are registered by attributes on the ID, and you cannot add them to
  `JsonSerializerOptions.Converters` or reuse them in another type.
