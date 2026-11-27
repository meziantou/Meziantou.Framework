---
title: Read-only document
description: Parse YAML into a compact read-only document over its UTF-8 bytes, navigate it and read values without allocating a tree.
---

[YamlReadOnlyDocument](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument) parses a YAML stream into one array of rows, one per node, over the UTF-8 input. It
builds no object per node and decodes no scalar until you read it, so it is the fastest way to look up values in a
document you do not change. To change a document, use the [document object model](05-DocumentObjectModel.md).

## Read a document

[Parse](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument.Parse) accepts a string, UTF-8 bytes or a stream. [Root](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument.Root) is the root node of the first document;
[GetDocumentRoot](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument.GetDocumentRoot) gives the others:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlReadOnlyDocumentTests.cs;region=YamlReadOnlyDocument_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var document = YamlReadOnlyDocument.Parse("""
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

Dispose the document when you are done: it returns its buffers to a pool, and its nodes cannot be used after that.
When you parse bytes, the document reads them in place: do not change them while you use it.

## Nodes

A [YamlReadOnlyNode](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode) is a small struct. [Kind](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Kind) tells whether it is a scalar, a mapping, a sequence or an
alias.

| To | Use |
| --- | --- |
| Read a scalar | [GetString](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.GetString), [TryGetInt64](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.TryGetInt64), [TryGetDouble](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.TryGetDouble), [TryGetBoolean](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.TryGetBoolean), [IsNull](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.IsNull) |
| Compare a scalar | [ValueEquals](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.ValueEquals), without allocating |
| Find a mapping value | The indexer, or [TryGetProperty](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.TryGetProperty) with a string or UTF-8 bytes |
| Enumerate | [EnumerateMapping](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.EnumerateMapping), [EnumerateSequence](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.EnumerateSequence), [GetLength](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.GetLength) |
| Follow an alias | [ResolveAlias](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.ResolveAlias): the node of its anchor, in the same document |
| Get the text of a node | [GetRawText](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.GetRawText), or [GetRawUtf8Text](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.GetRawUtf8Text) without allocating |
| Locate a node | [Start](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Start) and [End](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.End), or [Utf8Offset](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Utf8Offset) and [Utf8Length](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Utf8Length) in the UTF-8 bytes |
| Write a node | [WriteTo](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.WriteTo), with a [UTF-8 writer](12-Utf8Writer.md#copy-a-read-only-document) |

The number methods read plain scalars in the notation of the core schema; a quoted scalar is a string, and they return
`false` for it. [ValueEquals](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.ValueEquals) and [TryGetProperty](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.TryGetProperty) take a string, a span of
characters or UTF-8 bytes, and allocate nothing.

## Positions

A node has two kinds of positions. [Start](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Start) and [End](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.End) are [Mark](cref:Meziantou.Framework.Yaml.Mark) values, like the errors report:
the 0-based line and column, and the index in UTF-16 characters, which is the index in the string the document was
parsed from. [Utf8Offset](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Utf8Offset) and [Utf8Length](cref:Meziantou.Framework.Yaml.YamlReadOnlyNode.Utf8Length) count UTF-8 bytes in
[Utf8Text](cref:Meziantou.Framework.Yaml.YamlReadOnlyDocument.Utf8Text), even when the document was parsed from a string. They differ as soon as the text has a
character that is not ASCII:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlReadOnlyDocumentTests.cs;region=YamlReadOnlyDocument_Positions;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = "name: café\r\nemoji: 😀 # comment\r\nlist: [a, b]\r\n";
using var document = YamlReadOnlyDocument.Parse(yaml);
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
