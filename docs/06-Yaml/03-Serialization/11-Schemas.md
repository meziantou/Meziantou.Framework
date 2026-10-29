---
title: Schemas
description: Choose how untagged scalars such as yes, 0x10 or ~ are resolved, and write YAML that JSON-schema readers accept.
---

A plain scalar has no type in the YAML text: a schema decides whether `true`, `0x10`, `~` or `yes` is a boolean, a
number, null or a string. YAML 1.2 defines three schemas, and the library adds one:

| `YamlSchemaKind` | Resolves |
| --- | --- |
| `Failsafe` | Every plain scalar is a string. |
| `Json` | `null`, `true`, `false` and JSON numbers; any other plain scalar is invalid. |
| `Core` (default) | The JSON values, plus `~`, `Null`, `NULL`, `True`, `TRUE`, hexadecimal and octal integers, `.inf` and `.nan`. |
| `Extended` | The core values, plus `y`, `yes` and `on` booleans, `!!timestamp`, `!!merge` and `_` digit separators. |

## Apply a schema when reading

By default, the built-in converters use a fast and permissive reader, whatever the schema: it accepts the core
spellings of null, booleans, `.inf` and `.nan`, and also binary integers and digit separators. Set `UseSchema = true`
to apply the rules of the selected schema instead:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/SchemaTests.cs;region=YamlSchemaKind_Extended;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions { UseSchema = true, Schema = YamlSchemaKind.Extended };

var values = YamlSerializer.Deserialize<Dictionary<string, object>>("enabled: yes", options);
Assert.True(values!["enabled"] is true); // true, not "yes"
```
<!-- endref -->

With `Schema = YamlSchemaKind.Json` and `UseSchema = true`, a plain scalar that is not `null`, `true`, `false` or a JSON
number throws a `YamlException`: `name: text` is rejected, while `"name": "text"` is accepted.

An explicit `!!null`, `!!bool`, `!!int` or `!!float` tag is honored when reading into `object`, and a scalar that does
not match its tag throws a `YamlException`.

## Write JSON-compatible YAML

With `Schema = YamlSchemaKind.Json`, the writer double-quotes every scalar that is not null, a boolean or a number:
strings, keys, enum values, dates. The special floating-point values are written with an explicit `!!float` tag.

Whatever the schema, a `double`, `float` or `Half` value is written so that it reads back as a floating-point number,
and a string that the reader would take for another type is quoted: see
[Strings that look like other values](05-OutputFormat.md#strings-that-look-like-other-values).

## Schema classes

The schema classes `FailsafeSchema`, `JsonSchema`, `CoreSchema` and `ExtendedSchema`, in the
`Meziantou.Framework.Yaml.Schemas` namespace, derive from `SchemaBase` and implement `IYamlSchema`. They expand and
shorten tags, hold the rules that resolve scalars, and register tags. A `YamlValue` of the
[document object model](../05-DocumentObjectModel.md) takes one to choose the tag of its value. To build another schema,
derive from one of them and override `PrepareScalarRules`. The serializer itself selects its schema with
`YamlSchemaKind`.
