---
title: Anchors, aliases and merge keys
description: Write shared and cyclic references as anchors and aliases, read aliases back as the same object, and merge mappings with <<.
---

YAML can name a node with an anchor (`&name`) and repeat it with an alias (`*name`). The serializer uses them for
shared references when [ReferenceHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ReferenceHandling) asks for it. The examples of this page use this model:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlReferenceHandlingTests.cs;region=YamlReferenceHandling_Model;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Config
{
    public Section? First { get; set; }

    public Section? Second { get; set; }
}

internal sealed class Section
{
    public int Timeout { get; set; }

    public int Retries { get; set; }
}
```
<!-- endref -->

## Write shared references

By default, an object referenced twice is written twice, and a cycle fails when it reaches [MaxDepth](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.MaxDepth).
[ReferenceHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ReferenceHandling) changes this:

| Value | Behavior |
| --- | --- |
| [None](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.None) (default) | References are written in full every time. A cycle throws. |
| [Preserve](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.Preserve) | Every object gets an anchor, and the next references to it become aliases. Cycles are supported. |
| [PreserveMinimal](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.PreserveMinimal) | Only the objects referenced more than once, or part of a cycle, get an anchor. |

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlReferenceHandlingTests.cs;region=YamlReferenceHandling_PreserveMinimalOutput;format=md-fenced-code-block;dedent=true -->
```csharp
var shared = new Section { Timeout = 1 };
var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal };

var yaml = YamlSerializer.Serialize(new Config { First = shared, Second = shared }, options);
Assert.Equal("""
    First: &id001
      Timeout: 1
      Retries: 0
    Second: *id001

    """, yaml);

var config = YamlSerializer.Deserialize<Config>(yaml, options);
Assert.Same(config!.First, config.Second);
```
<!-- endref -->

[PreserveMinimal](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.PreserveMinimal) gives the more readable output. To find the shared objects, it serializes the value twice: the first
pass only collects the references, and its output is discarded. The serialization callbacks still run once per object,
but converters run in both passes: a converter with side effects can check [YamlWriter.IsCollectingReferences](cref:Meziantou.Framework.Yaml.Serialization.YamlWriter.IsCollectingReferences), which is
`true` during the first pass.

## Read aliases

Reading an alias requires [ReferenceHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ReferenceHandling) to be [Preserve](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.Preserve) or [PreserveMinimal](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.PreserveMinimal). The alias then gives the same
instance as its anchor:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlReferenceHandlingTests.cs;region=YamlReferenceHandling_Aliases;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

var config = YamlSerializer.Deserialize<Config>("""
    First: &shared
      Timeout: 30
    Second: *shared
    """, options);

Assert.Same(config!.First, config.Second);
```
<!-- endref -->

With the default [None](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.None), a document that contains an alias throws a [YamlException](cref:Meziantou.Framework.Yaml.YamlException) that says that aliases need
[ReferenceHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ReferenceHandling) to be [Preserve](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.Preserve). An anchor without alias is always accepted. To reject documents that use anchors
or aliases at all, see [Reading untrusted YAML](../09-UntrustedInput.md).

## Merge keys

A merge key (`<<`) copies the entries of other mappings into the current one. Its value is a mapping, an alias to a
mapping, or a sequence of them. The keys of the mapping itself win over the merged ones, and in a sequence the first
mappings win over the next ones:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlReferenceHandlingTests.cs;region=YamlReferenceHandling_MergeKeys;format=md-fenced-code-block;dedent=true -->
```csharp
var section = YamlSerializer.Deserialize<Section>("""
    <<: { Timeout: 30, Retries: 2 }
    Timeout: 60
    """);

Assert.Equal(60, section!.Timeout);
Assert.Equal(2, section.Retries);
```
<!-- endref -->

A merge key is usually an alias to a block of defaults:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlMergeKeyTests.cs;region=YamlMergeKey_Defaults;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = """
    defaults: &defaults
      timeout: 30
      retries: 2

    prod:
      <<: *defaults
      timeout: 60   # wins over the merged value
    """;

var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
var config = YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(yaml, options);

Assert.Equal(60, config!["prod"]["timeout"]);
Assert.Equal(2, config["prod"]["retries"]);
```
<!-- endref -->

- As for any alias, a merged alias requires [ReferenceHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ReferenceHandling) to be [Preserve](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.Preserve) or [PreserveMinimal](cref:Meziantou.Framework.Yaml.YamlReferenceHandling.PreserveMinimal).
- An alias merges the entries of the anchored mapping as written in the document, whatever type that mapping was read
  into.
- Merge keys belong to the [Core](cref:Meziantou.Framework.Yaml.YamlSchemaKind.Core) and [Extended](cref:Meziantou.Framework.Yaml.YamlSchemaKind.Extended) [schemas](11-Schemas.md); with the [Json](cref:Meziantou.Framework.Yaml.YamlSchemaKind.Json) and [Failsafe](cref:Meziantou.Framework.Yaml.YamlSchemaKind.Failsafe) schemas, `<<`
  is an ordinary key.
