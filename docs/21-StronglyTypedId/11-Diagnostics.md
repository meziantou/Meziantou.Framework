---
title: Diagnostics
description: The errors and warnings that the analyzer of the generator reports, and how to fix them.
---

The package includes an analyzer that reports the declarations that the generator cannot handle. When it reports an
error on a type, no code is generated for that type, so the errors that follow (a missing `FromInt32` or `Value`)
disappear once the first one is fixed.

| ID | Severity | Title |
| --- | --- | --- |
| [MFSTID0001](#mfstid0001-not-supported-type) | Error | Not supported type |
| [MFSTID0002](#mfstid0002-guid-generation-strategy-is-not-supported-by-the-target-framework) | Error | Guid generation strategy is not supported by the target framework |
| [MFSTID0003](#mfstid0003-guid-generation-strategy-is-only-applicable-to-guid) | Warning | Guid generation strategy is only applicable to Guid |
| [MFSTID0004](#mfstid0004-generic-types-are-not-supported) | Error | Generic types are not supported |

## MFSTID0001: Not supported type

The underlying type of the attribute is not one of the [supported types](04-UnderlyingTypes.md#supported-types), for
instance `[StronglyTypedId<TimeSpan>]`. The types that are only supported on some frameworks (`Int128`, `UInt128`,
`Half`) and `ObjectId` (which needs a reference to MongoDB.Bson) are reported when they are not available.

**Fix:** use a supported type. To wrap a value of another type, write the type by hand, or store a supported
representation of the value (a `long` number of ticks instead of a `TimeSpan`).

## MFSTID0002: Guid generation strategy is not supported by the target framework

`GuidGenerationStrategy = GuidGenerationStrategy.Version7` is set, on a type or in
[`StronglyTypedIdDefaults`](08-AssemblyDefaults.md), but the target framework has no `Guid.CreateVersion7()`, which
appeared in .NET 9.

**Fix:** target .NET 9 or later, or use `Version4` or `None`. In a project with several target frameworks, the error is
reported for the older ones.

## MFSTID0003: Guid generation strategy is only applicable to Guid

`GuidGenerationStrategy` is set on an ID that does not wrap a `Guid`. The option has no effect: only `Guid` IDs have a
`New()` method.

**Fix:** remove the option, or change the underlying type to `Guid`. A `GuidGenerationStrategy` set in
`StronglyTypedIdDefaults` is not reported: it applies to the `Guid` IDs of the assembly only.

## MFSTID0004: Generic types are not supported

The ID is generic (`ProjectId<T>`), or nested in a generic type. The converters are registered by attributes on the ID,
and an attribute argument cannot use a type parameter (CS0416), so the generator cannot register them.

**Fix:** declare the ID as a non-generic type, outside of generic types.
