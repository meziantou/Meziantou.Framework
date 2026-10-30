---
title: Diagnostics
description: The errors that the analyzer of the generator reports on FixedStringBuilderAttribute declarations.
---

The `Meziantou.Framework.FixedStringBuilder.Generator` package includes an analyzer that reports the declarations the
generator cannot handle. No member is generated for a type with an error, so the errors that follow (a missing
`Length` or conversion) disappear once the first one is fixed.

| ID | Severity | Title |
| --- | --- | --- |
| [MFFSG0001](#mffsg0001-fixedstringbuilderattribute-requires-one-argument) | Error | FixedStringBuilderAttribute requires one argument |
| [MFFSG0002](#mffsg0002-fixedstringbuilderattribute-argument-type-is-invalid) | Error | FixedStringBuilderAttribute argument type is invalid |
| [MFFSG0003](#mffsg0003-fixedstringbuilderattribute-length-must-be-positive) | Error | FixedStringBuilderAttribute length must be positive |
| [MFFSG0004](#mffsg0004-fixedstringbuilderattribute-length-is-too-large) | Error | FixedStringBuilderAttribute length is too large |
| [MFFSG0005](#mffsg0005-fixedstringbuilderattribute-requires-a-partial-struct) | Error | FixedStringBuilderAttribute requires a partial struct |
| [MFFSG0006](#mffsg0006-fixedstringbuilderattribute-does-not-support-readonly-or-ref-structs) | Error | FixedStringBuilderAttribute does not support readonly or ref structs |

## MFFSG0001: FixedStringBuilderAttribute requires one argument

The attribute has no argument, or more than one.

**Fix:** pass the capacity, and only the capacity: `[FixedStringBuilderAttribute(128)]`.

## MFFSG0002: FixedStringBuilderAttribute argument type is invalid

The argument is not an integer constant.

**Fix:** pass an `int` constant, a literal or a `const` field.

## MFFSG0003: FixedStringBuilderAttribute length must be positive

The capacity is 0 or negative.

**Fix:** pass a capacity greater than 0.

## MFFSG0004: FixedStringBuilderAttribute length is too large

The capacity is greater than 32,767, the maximum the generated types support.

**Fix:** pass a smaller capacity. For long texts, use a `StringBuilder` or a pooled buffer instead: a fixed string of
that size is copied every time it is passed by value.

## MFFSG0005: FixedStringBuilderAttribute requires a partial struct

The struct is not `partial`, so the generator cannot add its members.

**Fix:** add the `partial` modifier.

## MFFSG0006: FixedStringBuilderAttribute does not support readonly or ref structs

The struct is `readonly` or a `ref struct`. The generated members change the value and implement interfaces, which these
structs do not allow.

**Fix:** remove the `readonly` or `ref` modifier.
