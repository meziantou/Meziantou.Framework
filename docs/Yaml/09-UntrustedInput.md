---
title: Reading untrusted YAML
description: Limit the depth, anchors, aliases and alias expansion of documents from untrusted sources, and never create types named by the input.
---

YAML from an untrusted source, such as an upload or a request body, can be built to use a lot of memory or time: deep
nesting, or aliases that repeat large parts of the document. Configure the options to reject such documents:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_Hardening;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions
{
    MaxDepth = 32,
    AllowAnchors = false,
    AllowAliases = false,
    UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
};

Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: &id 1", options));
Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Price: 10", options));
```
<!-- endref -->

| Option | Default | Effect |
| --- | --- | --- |
| `MaxDepth` | `64` | Limits the nesting of mappings and sequences, when reading and writing. `0` means the default. |
| `AllowAnchors` | `true` | `false` rejects any document that declares an anchor. |
| `AllowAliases` | `true` | `false` rejects any document that uses an alias, which prevents alias expansion attacks. |
| `MaxAliasExpansionNodeCount` | `100000` | Limits the number of nodes that aliases create in the [document object model](05-DocumentObjectModel.md). `0` means the default. |
| `DuplicateKeyHandling` | `Error` | Rejects mappings with duplicate keys. |
| `UnmappedMemberHandling` | `Skip` | `Disallow` rejects keys that match no member. |
| `UnsafeAllowDeserializeFromTagTypeName` | `false` | Lets a YAML tag name a .NET type to create. |

A rejected document throws a `YamlException`: see [Handling errors](08-ErrorHandling.md).

## Document object model

The document object model expands each alias into a copy of the node of its anchor. An anchor that contains aliases to
another anchor, repeated a few times, makes the number of nodes grow exponentially while the text grows linearly.
`MaxDepth` does not prevent this, because the tree grows in breadth. `MaxAliasExpansionNodeCount` limits it: it counts
only the nodes created by aliases, so documents without aliases are never affected. The serializer applies the same
limit to the nodes that a merge key copies from an alias (`<<: *defaults`).

`YamlStream.Load` without options uses `YamlSerializerOptions.Default`. Pass your options when the YAML is untrusted:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlStream_LoadWithOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions { MaxAliasExpansionNodeCount = 1_000 };

using var reader = new StringReader("""
    base: &base { name: default }
    copy: *base
    """);

var stream = YamlStream.Load(reader, options);
```
<!-- endref -->

## Never create types named by the input

`UnsafeAllowDeserializeFromTagTypeName` lets a tag of the document choose the .NET type that the serializer creates.
With untrusted input, this lets the author of the document create types of your application that were never meant to
be created from YAML. Keep it disabled, and use [polymorphism](03-Serialization/08-Polymorphism.md) with registered
derived types instead: only the types you list can be created.
