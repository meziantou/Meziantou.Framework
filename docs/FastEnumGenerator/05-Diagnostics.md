---
title: Diagnostics
description: The errors and warnings reported by the analyzer of the package, and their code fixes.
---

The package includes an analyzer. It validates the `FastEnumAttribute` declarations, and reports the `System.Enum`
calls on the listed enums that have a generated equivalent. A code fix rewrites these calls, and adds the `using`
directive of `ExtensionMethodNamespace` when it is needed.

| ID | Severity | Title |
| --- | --- | --- |
| [MFEG0001](#mfeg0001-fastenum-target-type-is-invalid) | Error | FastEnum target type is invalid |
| [MFEG0002](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum Parse |
| [MFEG0003](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum TryParse |
| [MFEG0004](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum GetNames |
| [MFEG0005](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum GetValues |
| [MFEG0006](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum GetName |
| [MFEG0007](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum IsDefinedFast |
| [MFEG0008](#mfeg0002-to-mfeg0008-use-the-generated-member) | Warning | Use FastEnum ToStringFast |
| [MFEG0009](#mfeg0009-fastenum-target-enum-has-no-members) | Warning | FastEnum target enum has no members |

## MFEG0001: FastEnum target type is invalid

The type given to `FastEnumAttribute` is not an enum, or is `null`, for instance `[assembly: FastEnumAttribute(typeof(string))]`.
Nothing is generated for it.

**Fix:** pass the type of an enum.

## MFEG0002 to MFEG0008: Use the generated member

The code calls a `System.Enum` method on an enum listed in `FastEnumAttribute`, and the generator has written a
faster equivalent:

| ID | Call | Replacement |
| --- | --- | --- |
| MFEG0002 | `Enum.Parse(...)` | `TEnum.Parse(...)` |
| MFEG0003 | `Enum.TryParse(...)` | `TEnum.TryParse(...)` |
| MFEG0004 | `Enum.GetNames(...)` | `TEnum.GetNames(useMetadata: false)` |
| MFEG0005 | `Enum.GetValues(...)` | `TEnum.GetValues()` |
| MFEG0006 | `Enum.GetName(...)` | `value.GetName()` |
| MFEG0007 | `Enum.IsDefined(...)` | `TEnum.IsDefinedFast(...)` |
| MFEG0008 | `value.ToString()` | `value.ToStringFast()` |

MFEG0002, MFEG0003, MFEG0004, MFEG0005 and MFEG0007 suggest [static members](03-GeneratedMembers.md#static-members-c-14),
so they are reported only when the project uses C# 14 or later. The code fix is not offered when the replacement does
not fit the call site, for instance when the array returned by `Enum.GetNames` is assigned to a `string[]` variable,
which a `ReadOnlySpan<string>` cannot replace.

**Fix:** apply the code fix, or [enable interceptors](04-Interceptors.md) and suppress the rules, to keep the
`System.Enum` calls and run the generated code anyway.

## MFEG0009: FastEnum target enum has no members

The enum given to `FastEnumAttribute` declares no member. Nothing is generated for it, and the analyzer does not suggest
generated members for it.

**Fix:** remove the attribute, or add members to the enum.
