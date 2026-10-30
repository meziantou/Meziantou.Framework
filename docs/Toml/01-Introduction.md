---
title: Introduction
description: Read and write TOML 1.1 in .NET, as typed objects, as a document model or as a lossless syntax tree.
---

`Meziantou.Framework.Toml` reads and writes [TOML](https://toml.io) documents in .NET. It maps documents to your own
classes and records with an API shaped like `System.Text.Json`, reads them without a model as tables and arrays, and
parses them into a syntax tree that keeps every character, for tools that edit TOML files.

## What you can do with it

- **Serialize and deserialize objects**: classes, records and structs, collections, dictionaries, dates and times,
  enums, with naming policies, attributes, custom converters and polymorphism. See
  [Serialization](04-Serialization/01-Overview.md).
- **Generate the serialization code at build time** with the source generator that ships in the package, for NativeAOT,
  trimmed applications and errors reported when you build. See [Source generation](05-SourceGeneration.md).
- **Read and build documents without a model** with [TomlTable](cref:Meziantou.Framework.Toml.Model.TomlTable), [TomlArray](cref:Meziantou.Framework.Toml.Model.TomlArray) and [TomlTableArray](cref:Meziantou.Framework.Toml.Model.TomlTableArray), and keep the
  comments of a file you edit. See [Document model](07-DocumentModel.md).
- **Analyze or reformat TOML files** with a lossless syntax tree, a lexer and an event parser. See
  [Syntax tree, lexer and parser](08-SyntaxTreeAndParser.md).

## TOML versions

The library reads TOML 1.1, which accepts every TOML 1.0 document, and writes documents that TOML 1.0 readers can read
too. See [TOML 1.0 and 1.1](03-TomlVersions.md).

## Choose an API

| You want to | Use |
| --- | --- |
| Load a configuration file into your own types, or save them | [TomlSerializer](cref:Meziantou.Framework.Toml.TomlSerializer) with your types. See [Getting started](02-GettingStarted.md). |
| Do the same in a NativeAOT or trimmed application | [TomlSerializer](cref:Meziantou.Framework.Toml.TomlSerializer) with a [TomlSerializerContext](cref:Meziantou.Framework.Toml.Serialization.TomlSerializerContext). See [Source generation](05-SourceGeneration.md). |
| Read a document whose shape you don't know, or build one by code | [TomlSerializer](cref:Meziantou.Framework.Toml.TomlSerializer) with [TomlTable](cref:Meziantou.Framework.Toml.Model.TomlTable). See [Document model](07-DocumentModel.md). |
| Change a few values of a file and keep its comments | [TomlTable](cref:Meziantou.Framework.Toml.Model.TomlTable) with a [TomlMetadataStore](cref:Meziantou.Framework.Toml.Serialization.TomlMetadataStore). See [Document model](07-DocumentModel.md#edit-a-file-and-keep-its-comments). |
| Write a formatter, a linter or an editor feature | [SyntaxParser](cref:Meziantou.Framework.Toml.Parsing.SyntaxParser). See [Syntax tree, lexer and parser](08-SyntaxTreeAndParser.md). |

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Toml](cref:Meziantou.Framework.Toml) | [TomlSerializer](cref:Meziantou.Framework.Toml.TomlSerializer), [TomlSerializerOptions](cref:Meziantou.Framework.Toml.TomlSerializerOptions), [TomlTypeInfo\<T\>](cref:Meziantou.Framework.Toml.TomlTypeInfo{T}), [TomlDateTime](cref:Meziantou.Framework.Toml.TomlDateTime), [TomlException](cref:Meziantou.Framework.Toml.TomlException), the naming policies |
| [Meziantou.Framework.Toml.Serialization](cref:Meziantou.Framework.Toml.Serialization) | The attributes, [TomlSerializerContext](cref:Meziantou.Framework.Toml.Serialization.TomlSerializerContext), the converters, [TomlReader](cref:Meziantou.Framework.Toml.Serialization.TomlReader), [TomlWriter](cref:Meziantou.Framework.Toml.Serialization.TomlWriter), [TomlMetadataStore](cref:Meziantou.Framework.Toml.Serialization.TomlMetadataStore) |
| [Meziantou.Framework.Toml.Model](cref:Meziantou.Framework.Toml.Model) | The document model: [TomlTable](cref:Meziantou.Framework.Toml.Model.TomlTable), [TomlArray](cref:Meziantou.Framework.Toml.Model.TomlArray), [TomlTableArray](cref:Meziantou.Framework.Toml.Model.TomlTableArray) |
| [Meziantou.Framework.Toml.Parsing](cref:Meziantou.Framework.Toml.Parsing) | [SyntaxParser](cref:Meziantou.Framework.Toml.Parsing.SyntaxParser), [TomlLexer](cref:Meziantou.Framework.Toml.Parsing.TomlLexer), [TomlParser](cref:Meziantou.Framework.Toml.Parsing.TomlParser) |
| [Meziantou.Framework.Toml.Syntax](cref:Meziantou.Framework.Toml.Syntax) | The syntax tree: [DocumentSyntax](cref:Meziantou.Framework.Toml.Syntax.DocumentSyntax), [KeyValueSyntax](cref:Meziantou.Framework.Toml.Syntax.KeyValueSyntax), [SyntaxVisitor](cref:Meziantou.Framework.Toml.Syntax.SyntaxVisitor), the diagnostics |

The serializer has its own attributes: the `System.Text.Json.Serialization` attributes, such as `[JsonPropertyName]`,
are not recognized.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and read your first file.
- [Serialization](04-Serialization/01-Overview.md): map your types, configure the output, handle errors.
- [Source generation](05-SourceGeneration.md): when and how to generate the serialization code.
