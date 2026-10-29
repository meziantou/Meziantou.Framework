---
title: Overview
description: The rules of the TaggedValues analyzer, their code fixes, and how to configure them.
---

The analyzer of `Meziantou.Framework.TaggedValues` reports its diagnostics with the `MFTV` prefix. Every message names
both declarations and their tags, so a build log is enough to act on.

## Rules

| Rule | Reports | Severity | Code fix |
| -- | -- | :--: | -- |
| [MFTV0001](02-MFTV0001.md) | Values with different tags are compared | Warning | — |
| [MFTV0002](03-MFTV0002.md) | A value flows to a declaration that expects another tag | Warning | Change the tag of the target |
| [MFTV0003](04-MFTV0003.md) | Values with different tags are combined into one value | Warning | — |
| [MFTV0004](05-MFTV0004.md) | An override or an implementation has other tags than its base member | Warning | Change the tag of the override |
| [MFTV0005](06-MFTV0005.md) | A `[ValueTag]` attribute or a `ValueTag` comment is invalid | Warning | Remove the annotation |
| [MFTV0006](07-MFTV0006.md) | Two `Id` members get the same tag from the naming convention | Warning | — |
| [MFTV0007](08-MFTV0007.md) | A `[ValueTag]` attribute repeats the tag of the naming convention | Info | Remove the attribute |
| [MFTV0008](09-MFTV0008.md) | A method returns tagged values, but its return value is not tagged | Info | Add the tag |
| [MFTV0009](10-MFTV0009.md) | In strict mode, a tagged value is mixed with an untagged value | Warning | Add the tag |

MFTV0006 and MFTV0007 are only reported when the [naming conventions](../07-NamingConventions.md) are enabled, and
MFTV0009 only in [strict mode](../08-StrictMode.md).

## The examples of these pages

The examples of the rule pages use these types:

<!-- ref:../../../samples/Meziantou.Framework.TaggedValues.Samples/Rules/Model.cs;region=TaggedValues_RulesModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; }
    [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
}

public sealed class Project
{
    [ValueTag("ProjectId")] public Guid Id { get; set; }
}

public sealed class OrderRepository
{
    public Order? Find([ValueTag("OrderId")] Guid orderId) => null;
}
```
<!-- endref -->

## Code fixes

The code fixes change the tag of the target of a flow or of an override, add the suggested tag to a return value or to
an untagged declaration, and remove invalid or redundant annotations.

- A code fix that changes a tag changes every part of a partial member.
- The code fix does not change a tag inherited from a base member, or declared by an `[assembly: ValueTag]` attribute:
  fix the base member or the assembly attribute yourself.
- On a local variable, the code fix changes the `ValueTag` comment. When the comment also tags other variables of the
  declaration, it adds a comment next to the name of the variable instead.

## Configure the analyzer

The analyzer reads two options from the `.editorconfig` file:

| Option | Default | Effect |
| -- | :--: | -- |
| `taggedvalues.infer_tags_from_names` | `false` | Infers tags from the names of `Id`, `xxxId` and `id` declarations. See [Naming conventions](../07-NamingConventions.md). |
| `taggedvalues.strict` | `false` | Also reports tagged values mixed with untagged values. See [Strict mode](../08-StrictMode.md). |

The severity of each rule is configured like the severity of any Roslyn rule. For instance, to turn the suggestions to
tag return values into warnings, and to disable the comparison rule in the tests:

```editorconfig
[*.cs]
taggedvalues.infer_tags_from_names = true
dotnet_diagnostic.MFTV0008.severity = warning

[tests/**.cs]
dotnet_diagnostic.MFTV0001.severity = none
```
