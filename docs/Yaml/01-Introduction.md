---
title: Introduction
description: A YAML 1.2 parser, emitter and serializer for .NET, with a source generator for NativeAOT and trimming.
---

`Meziantou.Framework.Yaml` reads and writes YAML in .NET. It converts your classes to YAML and back, gives you a model of
a document to inspect or change it without a class, keeps the exact text of a file for tools that must not disturb it,
and generates the serialization code at build time for NativeAOT and trimmed applications.

## What you can do with it

- **Read and write configuration files and other YAML data as .NET objects**: classes, records, collections,
  dictionaries, enums and the common scalar types. See [Serialization](03-Serialization/01-Overview.md).
- **Choose the shape of the YAML**: naming policies, ignored members, indentation, flow or block collections, quoted or
  block strings. See [Members and names](03-Serialization/04-MembersAndNames.md) and
  [Output format](03-Serialization/05-OutputFormat.md).
- **Serialize class hierarchies and C# unions**, identified by a key or a YAML tag. See
  [Polymorphism](03-Serialization/08-Polymorphism.md) and [C# unions](03-Serialization/09-Unions.md).
- **Keep shared references** as anchors and aliases, and read merge keys (`<<`). See
  [Anchors, aliases and merge keys](03-Serialization/10-AnchorsAliasesAndMergeKeys.md).
- **Serialize without reflection**: the source generator ships in the package and creates the metadata at build time.
  See [Source generation](04-SourceGeneration.md).
- **Work with documents without a class**: load a document, navigate it, change it and write it back, keeping the
  comments and the layout of what did not change. See [Document object model](05-DocumentObjectModel.md).
- **Look up values fast** in a read-only document that builds no tree. See [Read-only document](11-ReadOnlyDocument.md).
- **Build tools** such as linters and formatters: the syntax tree keeps the comments and the whitespace, with the
  position of every token. See [Syntax tree](06-SyntaxTree.md).
- **Stream over large documents** event by event, and write YAML event by event. See
  [Parser and emitter](07-ParserAndEmitter.md).
- **Read UTF-8 bytes token by token** without allocating, with the position of every token, from a buffer or a stream.
  See [UTF-8 reader](10-Utf8Reader.md).
- **Write UTF-8 YAML node by node** to a stream or a buffer writer. See [UTF-8 writer](12-Utf8Writer.md).
- **Read untrusted documents safely**: limit the depth, the aliases and the anchors. See
  [Reading untrusted YAML](09-UntrustedInput.md).

## Supported YAML

The library implements **YAML 1.2**. A document that declares `%YAML 1.1`, `%YAML 1.2` or a later 1.x version is read
with the YAML 1.2 rules; other versions throw a [SemanticErrorException](cref:Meziantou.Framework.Yaml.SemanticErrorException). Streams of several documents, `%TAG`
directives, block and flow collections, the five scalar styles (plain, single-quoted, double-quoted, literal and
folded), anchors, aliases, merge keys, tags and comments are supported.

The parser, the emitter and the loader are tested against the [YAML test suite](https://github.com/yaml/yaml-test-suite):
the parser produces the expected events or rejects the invalid documents, the documents written by the emitter read
back as the same data, and the documents load as the expected JSON values.

## Choose an API

The library has several layers. Use the highest one that does what you need:

| To | Use | Page |
| --- | --- | --- |
| Convert YAML to your own types and back | [YamlSerializer](cref:Meziantou.Framework.Yaml.YamlSerializer) | [Serialization](03-Serialization/01-Overview.md) |
| Read or change a document without a type for it | [YamlStream](cref:Meziantou.Framework.Yaml.Model.YamlStream), [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping), [YamlSequence](cref:Meziantou.Framework.Yaml.Model.YamlSequence), [YamlValue](cref:Meziantou.Framework.Yaml.Model.YamlValue) | [Document object model](05-DocumentObjectModel.md) |
| Edit a file while keeping its comments and layout, or report positions | [YamlSyntaxTree](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree) | [Syntax tree](06-SyntaxTree.md) |
| Process a stream of parsing events, or write events | [Parser](cref:Meziantou.Framework.Yaml.Parser), [EventReader](cref:Meziantou.Framework.Yaml.EventReader), [Emitter](cref:Meziantou.Framework.Yaml.Emitter) | [Parser and emitter](07-ParserAndEmitter.md) |
| Look up values in a document you do not change | [YamlReadOnlyDocument](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument) | [Read-only document](11-ReadOnlyDocument.md) |
| Read tokens from UTF-8 bytes, with their positions | [Utf8YamlReader](cref:Meziantou.Framework.Yaml.Utf8YamlReader) | [UTF-8 reader](10-Utf8Reader.md) |
| Write YAML node by node as UTF-8 | [Utf8YamlWriter](cref:Meziantou.Framework.Yaml.Utf8YamlWriter) | [UTF-8 writer](12-Utf8Writer.md) |

The layers work together: a member of a serialized class can hold a document node, and a node converts to and from an
object.

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Yaml](cref:Meziantou.Framework.Yaml) | [YamlSerializer](cref:Meziantou.Framework.Yaml.YamlSerializer), [YamlSerializerOptions](cref:Meziantou.Framework.Yaml.YamlSerializerOptions), [YamlException](cref:Meziantou.Framework.Yaml.YamlException), the parser, the event reader, the emitter, [Utf8YamlReader](cref:Meziantou.Framework.Yaml.Utf8YamlReader), [Utf8YamlWriter](cref:Meziantou.Framework.Yaml.Utf8YamlWriter) and [YamlReadOnlyDocument](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument) |
| [Meziantou.Framework.Yaml.Serialization](cref:Meziantou.Framework.Yaml.Serialization) | The attributes, converters, [YamlReader](cref:Meziantou.Framework.Yaml.Serialization.YamlReader), [YamlWriter](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter) and [YamlSerializerContext](cref:Meziantou.Framework.Yaml.Serialization.YamlSerializerContext) |
| [Meziantou.Framework.Yaml.Model](cref:Meziantou.Framework.Yaml.Model) | The document object model: [YamlStream](cref:Meziantou.Framework.Yaml.Model.YamlStream), [YamlDocument](cref:Meziantou.Framework.Yaml.Model.YamlDocument), [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping), [YamlSequence](cref:Meziantou.Framework.Yaml.Model.YamlSequence), [YamlValue](cref:Meziantou.Framework.Yaml.Model.YamlValue) |
| [Meziantou.Framework.Yaml.Syntax](cref:Meziantou.Framework.Yaml.Syntax) | The syntax tree |
| [Meziantou.Framework.Yaml.Events](cref:Meziantou.Framework.Yaml.Events) | The parsing events |
| [Meziantou.Framework.Yaml.Schemas](cref:Meziantou.Framework.Yaml.Schemas) | The schemas that resolve untagged scalars |

## Next steps

- [Getting started](02-GettingStarted.md): install the package, serialize a class and read a document.
- [Serialization](03-Serialization/01-Overview.md): everything about converting objects.
- [Source generation](04-SourceGeneration.md): serialize in NativeAOT and trimmed applications.
