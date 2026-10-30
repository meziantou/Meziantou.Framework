---
title: Serialization
description: Convert .NET objects to YAML and back, and control the names, the output format, polymorphism and references.
---

[YamlSerializer](cref:Meziantou.Framework.Yaml.YamlSerializer) converts .NET objects to YAML and YAML to .NET objects. It writes the public properties of a class as a
mapping, collections as sequences, and the common .NET types as scalars. [YamlSerializerOptions](cref:Meziantou.Framework.Yaml.YamlSerializerOptions) and attributes on your
types control the result.

- [Serialize and deserialize](02-SerializeAndDeserialize.md): strings, files and streams, reusable options, reading
  without exceptions.
- [Supported types](03-SupportedTypes.md): the scalar types, collections and dictionaries, `object`, constructors and
  records.
- [Members and names](04-MembersAndNames.md): naming policies, renamed, ignored, ordered and non-public members, enum
  values.
- [Output format](05-OutputFormat.md): indentation, flow collections, sequence layout, string styles, key order.
- [Reading behavior](06-ReadingBehavior.md): unknown keys, required values, numbers in strings, filling existing
  collections, extra keys, callbacks.
- [Custom converters](07-CustomConverters.md): read and write a type your own way.
- [Polymorphism](08-Polymorphism.md): serialize a class hierarchy with a discriminator key or a YAML tag.
- [C# unions](09-Unions.md): serialize a union as its selected case.
- [Anchors, aliases and merge keys](10-AnchorsAliasesAndMergeKeys.md): shared references, cycles and `<<`.
- [Schemas](11-Schemas.md): how untagged scalars such as `yes`, `0x10` or `~` are resolved.

The serializer discovers your types with reflection. To serialize without reflection, in a NativeAOT or trimmed
application, generate the metadata at build time: see [Source generation](../04-SourceGeneration.md).
