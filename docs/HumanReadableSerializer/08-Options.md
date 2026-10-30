---
title: Options
description: The options of HumanReadableSerializerOptions, and how to reuse and copy an options instance.
---

| Option | Default | Description |
| --- | --- | --- |
| [DefaultIgnoreCondition](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.DefaultIgnoreCondition) | [Never](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.Never) | Condition that skips members. See [Members](04-Members.md#skip-default-and-empty-values) |
| [IncludeFields](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.IncludeFields) | `false` | Writes the public fields |
| [IncludeObsoleteMembers](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.IncludeObsoleteMembers) | `false` | Writes the members marked `[Obsolete]` |
| [PropertyOrder](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.PropertyOrder) | `null` | Comparer that sorts the members by name |
| [DictionaryKeyOrder](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.DictionaryKeyOrder) | `null` | Comparer that sorts the keys of dictionaries |
| [MaxDepth](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.MaxDepth) | `64` | Maximum nesting depth of objects and collections |
| [ShowInvisibleCharactersInValues](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.ShowInvisibleCharactersInValues) | `false` | Makes whitespace and control characters visible. See [Output format](03-OutputFormat.md#invisible-characters-and-line-endings) |
| [NewLine](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.NewLine) | `"\n"` | Line separator: `"\n"` or `"\r\n"` |
| [Converters](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.Converters) | Empty | Converters that take precedence over the built-in ones. See [Converters](05-Converters.md) |

The methods [AddAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddAttribute), [AddPropertyAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddPropertyAttribute), [AddFieldAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddFieldAttribute), [AddTypeAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddTypeAttribute), [IgnoreMember](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptionsExtensions.IgnoreMember),
[IgnoreMembersWithType](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptionsExtensions.IgnoreMembersWithType) and [IgnoreMembersThatThrow](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptionsExtensions.IgnoreMembersThatThrow) configure members ([Members](04-Members.md)), [AddFormatter](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddFormatter),
[AddJsonFormatter](cref:Meziantou.Framework.HumanReadable.ValueFormatters.FormatterExtensions.AddJsonFormatter), [AddXmlFormatter](cref:Meziantou.Framework.HumanReadable.ValueFormatters.FormatterExtensions.AddXmlFormatter), [AddUrlEncodedFormFormatter](cref:Meziantou.Framework.HumanReadable.ValueFormatters.FormatterExtensions.AddUrlEncodedFormFormatter) and [AddHtmlFormatter](cref:Meziantou.Framework.HumanReadable.ValueFormatters.HtmlFormatterExtensions.AddHtmlFormatter) configure formatters
([JSON, XML and HTML values](06-JsonXmlAndHtml.md)), and [AddHttpConverters](cref:Meziantou.Framework.HumanReadable.Converters.HumanReadableHttpExtensions.AddHttpConverters) configures HTTP messages
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
- The first serialization makes the instance read-only ([IsReadOnly](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.IsReadOnly)): changing a property, adding a converter or an
  attribute then throws an `InvalidOperationException`. [MakeReadOnly()](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.MakeReadOnly) does the same explicitly.
- [HumanReadableSerializerOptions](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions) is a record: `options with { ... }` creates a mutable copy, with the converters,
  attributes and formatters of the original, that you can change.
- A read-only instance can be used by several threads at the same time.

## Serialize as a type

`Serialize(value, type, options)` picks the converter of `type` instead of the converter of the runtime type of the
value. The value must be assignable to the type, or an `ArgumentException` is thrown.
