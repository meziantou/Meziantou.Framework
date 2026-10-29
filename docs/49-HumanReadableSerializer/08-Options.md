---
title: Options
description: The options of HumanReadableSerializerOptions, and how to reuse and copy an options instance.
---

| Option | Default | Description |
| --- | --- | --- |
| `DefaultIgnoreCondition` | `Never` | Condition that skips members. See [Members](04-Members.md#skip-default-and-empty-values) |
| `IncludeFields` | `false` | Writes the public fields |
| `IncludeObsoleteMembers` | `false` | Writes the members marked `[Obsolete]` |
| `PropertyOrder` | `null` | Comparer that sorts the members by name |
| `DictionaryKeyOrder` | `null` | Comparer that sorts the keys of dictionaries |
| `MaxDepth` | `64` | Maximum nesting depth of objects and collections |
| `ShowInvisibleCharactersInValues` | `false` | Makes whitespace and control characters visible. See [Output format](03-OutputFormat.md#invisible-characters-and-line-endings) |
| `NewLine` | `"\n"` | Line separator: `"\n"` or `"\r\n"` |
| `Converters` | Empty | Converters that take precedence over the built-in ones. See [Converters](05-Converters.md) |

The methods `AddAttribute`, `AddPropertyAttribute`, `AddFieldAttribute`, `AddTypeAttribute`, `IgnoreMember`,
`IgnoreMembersWithType` and `IgnoreMembersThatThrow` configure members ([Members](04-Members.md)), `AddFormatter`,
`AddJsonFormatter`, `AddXmlFormatter`, `AddUrlEncodedFormFormatter` and `AddHtmlFormatter` configure formatters
([JSON, XML and HTML values](06-JsonXmlAndHtml.md)), and `AddHttpConverters` configures HTTP messages
([HTTP messages](07-HttpMessages.md)).

`HumanReadableSerializer.Serialize(value)` without options uses a shared default instance.

## Reuse and copy options

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_ReadOnlyOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions { PropertyOrder = StringComparer.Ordinal };
_ = HumanReadableSerializer.Serialize(new { B = 1, A = 2 }, options);

// The options cannot change once they have been used...
Assert.True(options.IsReadOnly);
Assert.Throws<InvalidOperationException>(() => options.IncludeFields = true);

// ...but a copy can
var copy = options with { IncludeFields = true };
Assert.False(copy.IsReadOnly);
```
<!-- endref -->

- The options cache the converters and members of each type. Create an instance once and reuse it, for instance in a
  static field; an instance per call repeats the reflection work.
- The first serialization makes the instance read-only (`IsReadOnly`): changing a property, adding a converter or an
  attribute then throws an `InvalidOperationException`. `MakeReadOnly()` does the same explicitly.
- `HumanReadableSerializerOptions` is a record: `options with { ... }` creates a mutable copy, with the converters,
  attributes and formatters of the original, that you can change.
- A read-only instance can be used by several threads at the same time.

## Serialize as a type

`Serialize(value, type, options)` picks the converter of `type` instead of the converter of the runtime type of the
value. The value must be assignable to the type, or an `ArgumentException` is thrown.
