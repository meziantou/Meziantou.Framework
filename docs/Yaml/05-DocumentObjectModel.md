---
title: Document object model
description: Load YAML documents as a tree of nodes, navigate and change them, create documents, and convert nodes to objects.
---

The document object model represents YAML as a tree of nodes, without a .NET type for its content. Use it to read a few
values of a document, to process documents whose shape varies, or to change a document and write it back. Its types
are in the [Meziantou.Framework.Yaml.Model](cref:Meziantou.Framework.Yaml.Model) namespace.

| Type | Represents |
| --- | --- |
| `YamlStream` | The whole text: a list of `YamlDocument` |
| `YamlDocument` | One document; `Contents` is its root node |
| `YamlMapping` | A mapping: an ordered list of key and value pairs, also usable as a dictionary |
| `YamlSequence` | A sequence: a list of nodes |
| `YamlValue` | A scalar; `Value` is its text |

`YamlMapping`, `YamlSequence` and `YamlValue` derive from `YamlElement`, which has an `Anchor` and a `Tag`. All the
types derive from `YamlNode`.

## Load a document

`YamlStream.Load` reads a `TextReader`, such as a `StringReader` or a `StreamReader` over a file. Cast each node to
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

A `YamlStream` holds every document of the text, in order:

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

A `YamlMapping` enumerates its entries in document order, as key and value pairs. Its indexer, `TryGetValue`,
`ContainsKey` and `Remove` accept a `string` key, and a `YamlElement` key for keys that are not plain strings:

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

A `YamlValue` holds the text of the scalar: `80` is the string `"80"`. To get a typed value, convert the node with
[`ToObject<T>`](#convert-between-nodes-and-objects). `YamlMapping` and `YamlSequence` have a `Style`, `Block` or `Flow`,
and `YamlValue` has a `Style` among the scalar styles (plain, quoted, literal, folded).

## Change a document and write it

The collections are mutable: set, add, insert and remove entries, then write the stream with `WriteTo`:

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
- `YamlMapping` is also a list: `Insert` adds an entry at a given position.
- `WriteTo` writes to a `TextWriter`, or to an [emitter](07-ParserAndEmitter.md). `ToString` returns the YAML of a
  node, trimmed.
- The model keeps neither the comments nor the exact layout of the text: the output is written again from the nodes.
  To change a file while keeping them, use the [syntax tree](06-SyntaxTree.md#edit-a-file-without-changing-its-layout).
- `DeepClone` copies a node and all its children.

## Create a document

Create the nodes, put the root in a `YamlDocument`, and the document in a `YamlStream`:

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

`YamlNode.FromObject` serializes an object to a node, and `ToObject<T>` deserializes a node, with the same rules and
options as the [serializer](03-Serialization/01-Overview.md):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlNode_FromObject;format=md-fenced-code-block;dedent=true -->
```csharp
var product = new Product { Id = 1, DisplayName = "Sample product" };

var node = YamlNode.FromObject(product);
var back = node.ToObject<Product>();
Assert.Equal("Sample product", back!.DisplayName);
```
<!-- endref -->

A node can also be the type of a member of a serialized class: a `YamlMapping` member keeps a part of the document as
it is, while the rest is bound to typed members. [Extension data](03-Serialization/06-ReadingBehavior.md#extra-keys)
uses this to keep the unknown keys.
