---
title: UTF-8 writer
description: Write YAML node by node as UTF-8 to a stream or a buffer writer, with the simplest scalar style that keeps each value, tag directives and comments, and copy read-only documents.
---

[Utf8YamlWriter](cref:Meziantou.Framework.Yaml.Utf8YamlWriter) writes YAML as UTF-8, one node at a time, to a `Stream` or an `IBufferWriter<byte>`. It is
the counterpart of the [UTF-8 reader](10-Utf8Reader.md): use it to produce YAML without building objects first, for
instance from a database query or another format. To write your own types, use the
[serializer](03-Serialization/01-Overview.md).

## Write a document

A mapping alternates keys and values: [WritePropertyName](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WritePropertyName) writes a key, and the next node is its
value. [WriteStartMapping](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteStartMapping) and [WriteStartSequence](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteStartSequence) start a collection, and each one is ended by
[WriteEndMapping](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteEndMapping) or [WriteEndSequence](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteEndSequence):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Utf8YamlWriterTests.cs;region=Utf8YamlWriter_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new ArrayBufferWriter<byte>();
using (var writer = new Utf8YamlWriter(buffer))
{
    writer.WriteStartMapping();
    writer.WritePropertyName("name");
    writer.WriteStringValue("web");
    writer.WritePropertyName("replicas");
    writer.WriteNumberValue(3);
    writer.WritePropertyName("version");
    writer.WriteStringValue("1.0"); // quoted: plain 1.0 would be read as a number
    writer.WritePropertyName("ports");
    writer.WriteStartSequence(YamlStyle.Flow);
    writer.WriteNumberValue(80);
    writer.WriteNumberValue(443);
    writer.WriteEndSequence();
    writer.WritePropertyName("command");
    writer.WriteStringValue("echo hello\necho world\n", ScalarStyle.Literal);
    writer.WriteEndMapping();
}

Assert.Equal("""
    name: web
    replicas: 3
    version: '1.0'
    ports: [80, 443]
    command: |
      echo hello
      echo world

    """, Encoding.UTF8.GetString(buffer.WrittenSpan));
```
<!-- endref -->

The writer keeps the bytes in a buffer, and writes them to the destination when you call [Flush](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.Flush) or
[Dispose](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.Dispose), or when the buffer gets large. [BytesPending](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.BytesPending) and
[BytesCommitted](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.BytesCommitted) count the bytes on each side. The writer does not dispose the stream.

## Scalars

[WriteStringValue](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteStringValue) takes the value as a string or as UTF-8 bytes. By default, it writes a plain scalar
when the value reads back as the same string. Otherwise, it writes a single-quoted scalar, or a double-quoted one when the
value has a line break or a character that only an escape sequence can write:

| Value | Written |
| --- | --- |
| `web` | `web` |
| `1.0`, `true`, `null`, `yes` | `'1.0'`, `'true'`, `'null'`, `'yes'`: plain, they would read as a number, a boolean or a null |
| `a: b`, `#tag`, an empty string | `'a: b'`, `'#tag'`, `''` |
| `line 1` and `line 2` on two lines | `"line 1\nline 2"` |

Pass a [ScalarStyle](cref:Meziantou.Framework.Yaml.ScalarStyle) to choose another style. `Plain` writes `123` as is, so it reads back as a number.
`Literal` writes a block scalar that keeps the line breaks, `|` followed by the indented lines. When the style cannot
write the value, the writer falls back to double quotes. `Folded` is written as `Literal`.

[WriteNumberValue](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteNumberValue), [WriteBooleanValue](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteBooleanValue) and [WriteNullValue](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteNullValue) write plain
scalars of the core schema. A floating-point number always has a `.` or an exponent, so `5.0` stays a float, and the
special values are `.inf`, `-.inf` and `.nan`.

## Collections

A collection is written in the block style by default: one entry per line, indented by
[Utf8YamlWriterOptions.IndentSize](cref:Meziantou.Framework.Yaml.Utf8YamlWriterOptions.IndentSize) spaces (2 by default). A mapping or a sequence that is an item of a
sequence starts on the line of its `-`. Pass [YamlStyle.Flow](cref:Meziantou.Framework.Yaml.YamlStyle.Flow) to write a collection on one line, between
`[]` or `{}`. Inside a flow collection, every collection is a flow collection. An empty collection is written `[]` or
`{}`.

Any node can be a key. A collection key, and a scalar key longer than 1000 bytes, are written as explicit keys:
`? key` on one line, then `: value`.

## Anchors, tags and aliases

[WriteAnchor](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteAnchor) and [WriteTag](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteTag) set the properties of the next node, and
[WriteAliasValue](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteAliasValue) writes an alias (`*name`). A tag is given resolved: `tag:yaml.org,2002:str` is written
`!!str`, a local tag such as `!point` as is, and another URI as a verbatim tag (`!<tag:example.com,2000:point>`).

## Tag directives and comments

[WriteTagDirective](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteTagDirective) declares a `%TAG` directive for the next document: the tags that start with its prefix
are written with its handle, so `tag:example.com,2000:point` becomes `!e!point`. A directive is also the only way to
write a tag that is neither a local tag nor a URI, such as the `y-light` of `%TAG !y! y-`. The writer writes the `---`
that follows the directives, and a directive written after the root node of a document ends it with `...` and applies
to the next one.

[WriteComment](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteComment) writes a comment on its own line, before or after the root node, or between the entries of a
block collection at their indentation. Each line of the text is a comment line:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Utf8YamlWriterTests.cs;region=Utf8YamlWriter_TagDirectivesAndComments;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new ArrayBufferWriter<byte>();
using (var writer = new Utf8YamlWriter(buffer))
{
    writer.WriteTagDirective("!e!", "tag:example.com,2000:");
    writer.WriteComment("Generated file: do not edit");
    writer.WriteStartMapping();
    writer.WritePropertyName("origin");
    writer.WriteTag("tag:example.com,2000:point");
    writer.WriteStartMapping(YamlStyle.Flow);
    writer.WritePropertyName("left");
    writer.WriteNumberValue(0);
    writer.WritePropertyName("top");
    writer.WriteNumberValue(0);
    writer.WriteEndMapping();
    writer.WriteComment("The servers,\nin the order of preference");
    writer.WritePropertyName("servers");
    writer.WriteStartSequence();
    writer.WriteComment("none yet");
    writer.WriteEndSequence();
    writer.WriteEndMapping();
}

Assert.Equal("""
    %TAG !e! tag:example.com,2000:
    # Generated file: do not edit
    ---
    origin: !e!point {left: 0, top: 0}
    # The servers,
    # in the order of preference
    servers:
      # none yet
      []

    """, Encoding.UTF8.GetString(buffer.WrittenSpan));
```
<!-- endref -->

A comment cannot be written inside a flow collection, which is written on one line, or between a key and its value.

## Documents

The first document needs no marker. To write another one, call [WriteStartDocument](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteStartDocument), which writes
`---`. [WriteEndDocument](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteEndDocument) writes the `...` marker.

## Copy a read-only document

[YamlReadOnlyNode.WriteTo](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.WriteTo) writes a node of a [read-only document](11-ReadOnlyDocument.md) with its anchors, tags
and collection styles, and [YamlReadOnlyDocument.WriteTo](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument.WriteTo) writes all its documents:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Utf8YamlWriterTests.cs;region=Utf8YamlWriter_WriteTo;format=md-fenced-code-block;dedent=true -->
```csharp
using var document = YamlReadOnlyDocument.Parse("""
    name: web # a comment is not a node: it is not written
    settings: {replicas: 3, image: "nginx:1.27"}
    """);

using var stream = new MemoryStream();
using (var writer = new Utf8YamlWriter(stream))
{
    document.Root["settings"].WriteTo(writer);
}

Assert.Equal("{replicas: 3, image: \"nginx:1.27\"}\n", Encoding.UTF8.GetString(stream.ToArray()));
```
<!-- endref -->

A plain scalar stays plain, so it keeps its type. A quoted or block scalar may get another quoted style. The `%TAG`
directives of the documents are written, and their tags use the same handles. The comments and the layout are not
kept: the read-only document does not keep them. To change a document and keep its text, use the
[document object model](05-DocumentObjectModel.md#keep-the-formatting).

## Errors and limits

The writer checks the order of the calls: it throws an `InvalidOperationException` when a node cannot be written at
the current position, such as a property name in a sequence, a second root node without
[WriteStartDocument](cref:Meziantou.Framework.Yaml.Utf8YamlWriter.WriteStartDocument), or an end that does not match the start. It throws an `ArgumentException` for a
value that is not valid UTF-8 or UTF-16, an anchor name with a space or a flow indicator, a comment with a control
character, and a tag that is neither `!`, a local tag, a URI, nor starts with the prefix of a `%TAG` directive of the
document. [Utf8YamlWriterOptions.MaxDepth](cref:Meziantou.Framework.Yaml.Utf8YamlWriterOptions.MaxDepth) limits the nesting of the collections, 64 by
default.
