---
title: Document object model
description: Load YAML documents as a tree of nodes, navigate and change them, create documents, and convert nodes to objects.
---

The document object model represents YAML as a tree of nodes, without a .NET type for its content. Use it to read a few
values of a document, to process documents whose shape varies, or to change a document and write it back. Its types
are in the [Meziantou.Framework.Yaml.Model](cref:Meziantou.Framework.Yaml.Model) namespace.

| Type | Represents |
| --- | --- |
| [YamlStream](cref:Meziantou.Framework.Yaml.Model.YamlStream) | The whole text: a list of [YamlDocument](cref:Meziantou.Framework.Yaml.Model.YamlDocument) |
| [YamlDocument](cref:Meziantou.Framework.Yaml.Model.YamlDocument) | One document; [Contents](cref:Meziantou.Framework.Yaml.Model.YamlDocument.Contents) is its root node |
| [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping) | A mapping: an ordered list of key and value pairs, also usable as a dictionary |
| [YamlSequence](cref:Meziantou.Framework.Yaml.Model.YamlSequence) | A sequence: a list of nodes |
| [YamlValue](cref:Meziantou.Framework.Yaml.Model.YamlValue) | A scalar; [Value](cref:Meziantou.Framework.Yaml.Model.YamlValue.Value) is its text |

[YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping), [YamlSequence](cref:Meziantou.Framework.Yaml.Model.YamlSequence) and [YamlValue](cref:Meziantou.Framework.Yaml.Model.YamlValue) derive from [YamlElement](cref:Meziantou.Framework.Yaml.Model.YamlElement), which has an [Anchor](cref:Meziantou.Framework.Yaml.Model.YamlElement.Anchor) and a [Tag](cref:Meziantou.Framework.Yaml.Model.YamlElement.Tag). All the
types derive from [YamlNode](cref:Meziantou.Framework.Yaml.Model.YamlNode).

## Load a document

[YamlStream.Load](cref:Meziantou.Framework.Yaml.Model.YamlStream.Load) reads a `TextReader`, such as a `StringReader` or a `StreamReader` over a file. Cast each node to
its type to read it:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlStream_Load;format=md-fenced-code-block;dedent=true -->
```csharp
var stream = YamlStream.Load(new StringReader("""
    product:
      id: 1
      name: Sample product
    """));

var document = stream[0];
var root = (YamlMapping)document.Contents!;
var product = (YamlMapping)root["product"]!;
var name = ((YamlValue)product["name"]!).Value;
Assert.Equal("Sample product", name);
```
<!-- endref -->

The model expands each alias into a copy of the node of its anchor. When the YAML comes from an untrusted source, pass
the options that limit this expansion: see [Reading untrusted YAML](09-UntrustedInput.md#document-object-model).

## Read several documents

A [YamlStream](cref:Meziantou.Framework.Yaml.Model.YamlStream) holds every document of the text, in order:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlStream_MultipleDocuments;format=md-fenced-code-block;dedent=true -->
```csharp
var stream = YamlStream.Load(new StringReader("""
    name: first
    ---
    name: second
    """));

var names = new List<string>();
foreach (var document in stream)
{
    var root = (YamlMapping)document.Contents!;
    names.Add(((YamlValue)root["name"]!).Value);
}

Assert.Equal(["first", "second"], names);
```
<!-- endref -->

## Navigate a document

A [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping) enumerates its entries in document order, as key and value pairs. Its indexer, [TryGetValue](cref:Meziantou.Framework.Yaml.Model.YamlMapping.TryGetValue),
[ContainsKey](cref:Meziantou.Framework.Yaml.Model.YamlMapping.ContainsKey) and [Remove](cref:Meziantou.Framework.Yaml.Model.YamlMapping.Remove) accept a `string` key, and a [YamlElement](cref:Meziantou.Framework.Yaml.Model.YamlElement) key for keys that are not plain strings:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlMapping_Navigate;format=md-fenced-code-block;dedent=true -->
```csharp
var stream = YamlStream.Load(new StringReader("""
    services:
      web:
        image: nginx
        ports: [80, 443]
      cache:
        image: redis
    """));

var services = (YamlMapping)((YamlMapping)stream[0].Contents!)["services"]!;

var images = new List<string>();
foreach (var (name, service) in services)
{
    var image = ((YamlMapping)service!)["image"] as YamlValue;
    images.Add($"{((YamlValue)name).Value}={image?.Value}");
}

Assert.Equal(["web=nginx", "cache=redis"], images);

var web = (YamlMapping)services["web"]!;
Assert.True(web.TryGetValue("ports", out var ports));
Assert.Equal(YamlStyle.Flow, ((YamlSequence)ports).Style);
Assert.Equal(["80", "443"], ((YamlSequence)ports).Select(port => ((YamlValue)port).Value));
```
<!-- endref -->

A [YamlValue](cref:Meziantou.Framework.Yaml.Model.YamlValue) holds the text of the scalar: `80` is the string `"80"`. To get a typed value, convert the node with
[`ToObject<T>`](#convert-between-nodes-and-objects). [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping) and [YamlSequence](cref:Meziantou.Framework.Yaml.Model.YamlSequence) have a `Style`, [Block](cref:Meziantou.Framework.Yaml.YamlStyle.Block) or [Flow](cref:Meziantou.Framework.Yaml.YamlStyle.Flow),
and [YamlValue](cref:Meziantou.Framework.Yaml.Model.YamlValue) has a `Style` among the scalar styles (plain, quoted, literal, folded).

## Change a document and write it

The collections are mutable: set, add, insert and remove entries, then write the stream with [WriteTo](cref:Meziantou.Framework.Yaml.Model.YamlNode.WriteTo):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlMapping_Edit;format=md-fenced-code-block;dedent=true -->
```csharp
var stream = YamlStream.Load(new StringReader("""
    name: api
    replicas: 2
    ports:
      - 80
    """));

var root = (YamlMapping)stream[0].Contents!;
root["replicas"] = new YamlValue(3);
((YamlSequence)root["ports"]!).Add(new YamlValue(443));
root.Remove("name");
root["image"] = new YamlValue("nginx");

using var writer = new StringWriter { NewLine = "\n" };
stream.WriteTo(writer);
Assert.Equal("""
    replicas: 3
    ports:
      - 80
      - 443
    image: nginx

    """, writer.ToString());
```
<!-- endref -->

- `new YamlValue(value)` accepts a string, a number, a boolean, an enum or any other value, and writes it with the
  invariant culture.
- [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping) is also a list: [Insert](cref:Meziantou.Framework.Yaml.Model.YamlMapping.Insert) adds an entry at a given position.
- [WriteTo](cref:Meziantou.Framework.Yaml.Model.YamlNode.WriteTo) writes to a `TextWriter`, or to an [emitter](07-ParserAndEmitter.md). `ToString` returns the YAML of a
  node, trimmed.
- [WriteTo](cref:Meziantou.Framework.Yaml.Model.YamlNode.WriteTo) writes the nodes again, without the comments and the layout of the text. To keep them, see
  [Keep the formatting](#keep-the-formatting).
- [DeepClone](cref:Meziantou.Framework.Yaml.Model.YamlNode.DeepClone) copies a node and all its children.

## Keep the formatting

A stream loaded from a text remembers it. [ToStringPreservingFormatting](cref:Meziantou.Framework.Yaml.Model.YamlStream.ToStringPreservingFormatting) and
[WritePreservingFormatting](cref:Meziantou.Framework.Yaml.Model.YamlStream.WritePreservingFormatting) write that text with only the nodes that changed rewritten: the comments, the
blank lines, the quotes and the indentation of the rest are copied as they were.

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Model/YamlStreamPreservingFormattingTests.cs;region=YamlStream_PreservingFormatting;format=md-fenced-code-block;dedent=true -->
```csharp
var stream = YamlStream.Load(new StringReader("""
    # Deployment settings
    image: nginx:1.25   # pinned version
    replicas: 2
    env:
      LEVEL: info
    """));

var root = (YamlMapping)stream[0].Contents!;
root["image"] = new YamlValue("nginx:1.27");
((YamlMapping)root["env"]!).Add(new YamlValue("MODE"), new YamlValue("fast"));

// The comments and the formatting of the nodes that did not change are kept
Assert.Equal("""
    # Deployment settings
    image: nginx:1.27   # pinned version
    replicas: 2
    env:
      LEVEL: info
      MODE: fast
    """, stream.ToStringPreservingFormatting());
```
<!-- endref -->

- A changed scalar replaces the text of the scalar; the comment after it stays.
- An entry added to a block mapping, or an item added to a block sequence, is written on new lines at the indentation
  of the others. A removed entry or item removes its lines.
- A change the text cannot take in place, such as entries in a new order or a change in a flow collection, rewrites the
  smallest node that contains it, in the style of [WriteTo](cref:Meziantou.Framework.Yaml.Model.YamlNode.WriteTo).
- The model copies the node of an anchor for each of its aliases. When a copy or the anchored node changes, the alias
  is replaced by the content of the copy.
- A stream that was not loaded from a text, or whose list of documents changed, is written like
  [WriteTo](cref:Meziantou.Framework.Yaml.Model.YamlNode.WriteTo).

The [syntax tree](06-SyntaxTree.md#edit-a-file-without-changing-its-layout) is the other way to edit a file without
changing its layout: it gives the tokens and the trivia, and you replace text yourself.

## Create a document

Create the nodes, put the root in a [YamlDocument](cref:Meziantou.Framework.Yaml.Model.YamlDocument), and the document in a [YamlStream](cref:Meziantou.Framework.Yaml.Model.YamlStream):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlMapping_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var ports = new YamlSequence { new YamlValue(80), new YamlValue(443) };
ports.Style = YamlStyle.Flow;

var root = new YamlMapping
{
    { new YamlValue("name"), new YamlValue("api") },
    { new YamlValue("ports"), ports },
    { new YamlValue("debug"), new YamlValue(false) },
};

var stream = new YamlStream { new YamlDocument { Contents = root } };

using var writer = new StringWriter { NewLine = "\n" };
stream.WriteTo(writer);
Assert.Equal("""
    name: api
    ports: [80, 443]
    debug: false

    """, writer.ToString());
```
<!-- endref -->

The `Style` of a collection selects the block or the flow style (`[80, 443]`).

## Convert between nodes and objects

[YamlNode.FromObject](cref:Meziantou.Framework.Yaml.Model.YamlNode.FromObject) serializes an object to a node, and [ToObject\<T\>](cref:Meziantou.Framework.Yaml.Model.YamlNode.ToObject) deserializes a node, with the same rules and
options as the [serializer](03-Serialization/01-Overview.md):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlNode_FromObject;format=md-fenced-code-block;dedent=true -->
```csharp
var product = new Product { Id = 1, DisplayName = "Sample product" };

var node = YamlNode.FromObject(product);
var back = node.ToObject<Product>();
Assert.Equal("Sample product", back!.DisplayName);
```
<!-- endref -->

A node can also be the type of a member of a serialized class: a [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping) member keeps a part of the document as
it is, while the rest is bound to typed members. [Extension data](03-Serialization/06-ReadingBehavior.md#extra-keys)
uses this to keep the unknown keys.
