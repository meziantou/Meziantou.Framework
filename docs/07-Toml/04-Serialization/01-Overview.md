---
title: Serialization
description: Map TOML documents to .NET objects and back, configure the output, and handle invalid input.
---

`TomlSerializer` converts .NET objects to TOML documents and back. Its API follows `System.Text.Json`: `Serialize`,
`Deserialize` and `TryDeserialize`, an options record, attributes on your types, converters and polymorphism.

In short:

- A class, a record or a struct is a table: each public property is a key. Nested objects are tables too, and
  collections of objects are arrays of tables.
- `TomlSerializerOptions` sets the naming policy, what is skipped, how strict reading is and how the output looks. Create
  it once and reuse it: it caches the metadata of your types.
- Attributes override the options for one type or member: `[TomlPropertyName]`, `[TomlIgnore]`, `[TomlRequired]`, …
- The metadata that describes your types comes from reflection, by default, or from a source-generated
  `TomlSerializerContext`, which NativeAOT and trimmed applications need. See
  [Source generation](../05-SourceGeneration.md).

## Pages of this group

- [Serializing objects](02-SerializingObjects.md): the supported types, collections, dictionaries, dates and times,
  files and streams.
- [Options](03-Options.md): naming policies, what is written, strict reading, and the layout of the output.
- [Mapping members](04-MappingMembers.md): rename, ignore and require members, constructors, populating existing
  values, callbacks and extension data.
- [Custom converters](05-Converters.md): convert a type your own way, and write enums as names.
- [Polymorphism](06-Polymorphism.md): read and write derived types with a discriminator key.
- [Handling errors](07-ErrorHandling.md): exceptions, `TryDeserialize`, and limits for untrusted input.
