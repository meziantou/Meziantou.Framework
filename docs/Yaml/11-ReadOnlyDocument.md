---
title: Read-only document
description: Parse YAML into a compact read-only document over its UTF-8 bytes, navigate it and read values without allocating a tree.
---

[YamlDocument](cref:Meziantou.Framework.Yaml.YamlDocument) parses a YAML stream into one array of rows, one per node, over the UTF-8 input. It
builds no object per node and decodes no scalar until you read it, so it is the fastest way to look up values in a
document you do not change. To change a document, use the [document object model](05-DocumentObjectModel.md).

## Read a document

[Parse](cref:Meziantou.Framework.Yaml.YamlDocument.Parse) accepts a string, UTF-8 bytes or a stream. [Root](cref:Meziantou.Framework.Yaml.YamlDocument.Root) is the root node of the first document;
[GetDocumentRoot](cref:Meziantou.Framework.Yaml.YamlDocument.GetDocumentRoot) gives the others:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlDocumentTests.cs;region=YamlDocument_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var document = YamlDocument.Parse("""
    services:
      web:
        image: nginx
        replicas: 3
        ports: [80, 443]
    """);

var web = document.Root["services"]["web"];
Assert.Equal("nginx", web["image"].GetString());
Assert.True(web["replicas"].TryGetInt64(out var replicas));
Assert.Equal(3, replicas);

var ports = new List<long>();
foreach (var port in web["ports"].EnumerateSequence())
{
    Assert.True(port.TryGetInt64(out var value));
    ports.Add(value);
}

Assert.Equal([80L, 443L], ports);

// A key can be matched against UTF-8 bytes without allocating a string
Assert.True(web.TryGetProperty("image"u8, out var image));
Assert.Equal("nginx", image.GetRawText());
```
<!-- endref -->

Dispose the document when you are done: it returns its buffers to a pool, and its nodes cannot be used after that. To
keep a node, [clone it](#keep-a-node). When you parse bytes, the document reads them in place: do not change them
while you use it.

## Nodes

A [YamlElement](cref:Meziantou.Framework.Yaml.YamlElement) is a small struct. [Kind](cref:Meziantou.Framework.Yaml.YamlElement.Kind) tells whether it is a scalar, a mapping, a sequence or an
alias.

| To | Use |
| --- | --- |
| Read a scalar | [GetString](cref:Meziantou.Framework.Yaml.YamlElement.GetString), [TryGetInt64](cref:Meziantou.Framework.Yaml.YamlElement.TryGetInt64), [TryGetDouble](cref:Meziantou.Framework.Yaml.YamlElement.TryGetDouble), [TryGetBoolean](cref:Meziantou.Framework.Yaml.YamlElement.TryGetBoolean), [IsNull](cref:Meziantou.Framework.Yaml.YamlElement.IsNull) |
| Compare a scalar | [ValueEquals](cref:Meziantou.Framework.Yaml.YamlElement.ValueEquals), without allocating |
| Find a mapping value | The indexer, or [TryGetProperty](cref:Meziantou.Framework.Yaml.YamlElement.TryGetProperty) with a string or UTF-8 bytes |
| Enumerate | [EnumerateMapping](cref:Meziantou.Framework.Yaml.YamlElement.EnumerateMapping), [EnumerateSequence](cref:Meziantou.Framework.Yaml.YamlElement.EnumerateSequence), [GetLength](cref:Meziantou.Framework.Yaml.YamlElement.GetLength) |
| Follow an alias | [ResolveAlias](cref:Meziantou.Framework.Yaml.YamlElement.ResolveAlias): the node of its anchor, in the same document |
| Get the text of a node | [GetRawText](cref:Meziantou.Framework.Yaml.YamlElement.GetRawText), or [GetRawUtf8Text](cref:Meziantou.Framework.Yaml.YamlElement.GetRawUtf8Text) without allocating |
| Locate a node | [Start](cref:Meziantou.Framework.Yaml.YamlElement.Start) and [End](cref:Meziantou.Framework.Yaml.YamlElement.End), or [Utf8Offset](cref:Meziantou.Framework.Yaml.YamlElement.Utf8Offset) and [Utf8Length](cref:Meziantou.Framework.Yaml.YamlElement.Utf8Length) in the UTF-8 bytes |
| Write a node | [WriteTo](cref:Meziantou.Framework.Yaml.YamlElement.WriteTo), with a [UTF-8 writer](12-Utf8Writer.md#copy-a-read-only-document) |
| Keep a node after the document is disposed | [Clone](cref:Meziantou.Framework.Yaml.YamlElement.Clone) |

The number methods read plain scalars in the notation of the core schema; a quoted scalar is a string, and they return
`false` for it. [ValueEquals](cref:Meziantou.Framework.Yaml.YamlElement.ValueEquals) and [TryGetProperty](cref:Meziantou.Framework.Yaml.YamlElement.TryGetProperty) take a string, a span of
characters or UTF-8 bytes, and allocate nothing.

## Keep a node

[Clone](cref:Meziantou.Framework.Yaml.YamlElement.Clone) copies a node to a document of its own, like `JsonElement.Clone` in System.Text.Json. The copy owns its
memory, so it stays valid after the document is disposed, and it does not need to be disposed. It holds the lines of
the node, not the whole input, with the nodes of the anchors that its aliases refer to. It keeps the positions of the
node in the input. The clone of a copy is the copy itself.

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlDocumentTests.cs;region=YamlDocument_Clone;format=md-fenced-code-block;dedent=true -->
```csharp
YamlElement settings;
using (var document = YamlDocument.Parse("""
    defaults: &defaults
      timeout: 30
    settings:
      retries: 3
      base: *defaults
    """))
{
    // The copy owns its memory: it stays valid after the document is disposed
    settings = document.Root["settings"].Clone();
}

Assert.True(settings["retries"].TryGetInt64(out var retries));
Assert.Equal(3, retries);

// The anchor of an alias is copied with the node, and the positions are the ones of the input
Assert.True(settings["base"].ResolveAlias()["timeout"].TryGetInt64(out var timeout));
Assert.Equal(30, timeout);
Assert.Equal(3, settings.Start.Line);
```
<!-- endref -->

## Positions

A node has two kinds of positions. [Start](cref:Meziantou.Framework.Yaml.YamlElement.Start) and [End](cref:Meziantou.Framework.Yaml.YamlElement.End) are [Mark](cref:Meziantou.Framework.Yaml.Mark) values, like the errors report:
the 0-based line and column, and the index in UTF-16 characters, which is the index in the string the document was
parsed from. [Utf8Offset](cref:Meziantou.Framework.Yaml.YamlElement.Utf8Offset) and [Utf8Length](cref:Meziantou.Framework.Yaml.YamlElement.Utf8Length) count UTF-8 bytes in
[Utf8Text](cref:Meziantou.Framework.Yaml.YamlDocument.Utf8Text), even when the document was parsed from a string. They differ as soon as the text has a
character that is not ASCII:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlDocumentTests.cs;region=YamlDocument_Positions;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = "name: café\r\nemoji: 😀 # comment\r\nlist: [a, b]\r\n";
using var document = YamlDocument.Parse(yaml);
var list = document.Root["list"];

// Start and End are UTF-16 positions: they slice the string the document was parsed from
Assert.Equal("[a, b]", yaml[list.Start.Index..list.End.Index]);
Assert.Equal(2, list.Start.Line);
Assert.Equal(6, list.Start.Column);

// Utf8Offset and Utf8Length are positions in the UTF-8 bytes: "é" and "😀" are more bytes than characters
Assert.Equal(list.Start.Index + 3, list.Utf8Offset);
Assert.Equal("[a, b]"u8, list.GetRawUtf8Text());
```
<!-- endref -->

A position includes the anchor and the tag of the node. The lines are indexed the first time a node is located, so
the next locations are fast.
