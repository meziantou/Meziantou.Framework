---
title: Parser and emitter
description: Read YAML as a stream of parsing events without building a tree, and write YAML event by event.
---

The parser turns YAML into a sequence of events: the start and end of the stream, of each document, mapping and
sequence, and the scalars and aliases between them. The emitter does the opposite. This is the lowest layer of the
library: it builds no tree, so it suits large documents and code that only needs a few values. [Parser](cref:Meziantou.Framework.Yaml.Parser),
[EventReader](cref:Meziantou.Framework.Yaml.EventReader) and [Emitter](cref:Meziantou.Framework.Yaml.Emitter) are in the [Meziantou.Framework.Yaml](cref:Meziantou.Framework.Yaml) namespace, and the events in
[Meziantou.Framework.Yaml.Events](cref:Meziantou.Framework.Yaml.Events).

The parser creates an object for each event. To read UTF-8 bytes without allocating, or to get the exact position of
every token, use the [UTF-8 reader](10-Utf8Reader.md) the parser is built on. To write UTF-8 YAML without creating
events, use the [UTF-8 writer](12-Utf8Writer.md).

## Read events

[Parser.CreateParser](cref:Meziantou.Framework.Yaml.Parser.CreateParser) reads a `TextReader`. Wrap the parser in an [EventReader](cref:Meziantou.Framework.Yaml.EventReader) to consume the events with
expectations. This example lists the keys of the root mapping of each document:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/EventReaderTests.cs;region=EventReader_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = """
    name: first
    items: [1, 2]
    ---
    name: second
    """;

var parser = Parser.CreateParser(new StringReader(yaml));
var reader = new EventReader(parser);

var keys = new List<string>();
reader.Expect<StreamStart>();
while (!reader.Accept<StreamEnd>())
{
    reader.Expect<DocumentStart>();
    reader.Expect<MappingStart>();
    while (reader.Allow<MappingEnd>() is null)
    {
        keys.Add(reader.Expect<Scalar>().Value);
        reader.Skip(); // Skip the value, and its children for a collection
    }

    reader.Expect<DocumentEnd>();
}

Assert.Equal(["name", "items", "name"], keys);
```
<!-- endref -->

| Method | Does |
| --- | --- |
| [Expect\<T\>()](cref:Meziantou.Framework.Yaml.EventReader.Expect) | Returns the current event and moves to the next one; throws when the event is not a `T`. |
| [Allow\<T\>()](cref:Meziantou.Framework.Yaml.EventReader.Allow) | Same, but returns `null` without moving when the event is not a `T`. |
| [Accept\<T\>()](cref:Meziantou.Framework.Yaml.EventReader.Accept) | Tells whether the current event is a `T`, without moving. |
| [Peek\<T\>()](cref:Meziantou.Framework.Yaml.EventReader.Peek) | Returns the current event when it is a `T`, without moving. |
| [Skip()](cref:Meziantou.Framework.Yaml.EventReader.Skip) | Skips the current event and, for a mapping or a sequence, all its children. |

[CurrentDepth](cref:Meziantou.Framework.Yaml.EventReader.CurrentDepth) gives the nesting depth of the current event, and `Skip(untilDepth)` skips events until that depth.

To load a part of the stream as a [node](05-DocumentObjectModel.md), call [YamlStreamDocument.Load](cref:Meziantou.Framework.Yaml.Nodes.YamlStreamDocument.Load), [YamlMapping.Load](cref:Meziantou.Framework.Yaml.Nodes.YamlMapping.Load),
[YamlSequence.Load](cref:Meziantou.Framework.Yaml.Nodes.YamlSequence.Load) or [YamlValue.Load](cref:Meziantou.Framework.Yaml.Nodes.YamlValue.Load) with the [EventReader](cref:Meziantou.Framework.Yaml.EventReader) positioned on it.

## Write events

[Emitter](cref:Meziantou.Framework.Yaml.Emitter) writes events to a `TextWriter`. The events must form a valid stream: a [StreamStart](cref:Meziantou.Framework.Yaml.Events.StreamStart), documents that
contain one node each, and a [StreamEnd](cref:Meziantou.Framework.Yaml.Events.StreamEnd):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/EmitterTests.cs;region=Emitter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var writer = new StringWriter { NewLine = "\n" };
var emitter = new Emitter(writer);

emitter.Emit(new StreamStart());
emitter.Emit(new DocumentStart());
emitter.Emit(new MappingStart());
emitter.Emit(new Scalar("name"));
emitter.Emit(new Scalar("api"));
emitter.Emit(new Scalar("ports"));
emitter.Emit(new SequenceStart(anchor: null, tag: null, isImplicit: true, YamlStyle.Flow));
emitter.Emit(new Scalar("80"));
emitter.Emit(new Scalar("443"));
emitter.Emit(new SequenceEnd());
emitter.Emit(new MappingEnd());
emitter.Emit(new DocumentEnd(isImplicit: true));
emitter.Emit(new StreamEnd());

Assert.Equal("name: api\nports: [80, 443]\n", writer.ToString());
```
<!-- endref -->

- The constructor of [Emitter](cref:Meziantou.Framework.Yaml.Emitter) sets the indentation (`bestIndent`, `2` by default), the maximum line width
  (`bestWidth`, unlimited by default), and the canonical mode (`isCanonical`).
- The emitter ends lines with the `NewLine` of the `TextWriter`; the example sets it to `\n` to get the same text on
  every platform.
- The emitter keeps the data of the events, not always their presentation. When the requested style cannot write a
  value, it uses another one: a multi-line plain scalar is written quoted, and a literal or folded scalar that a block
  scalar cannot represent is written double-quoted. An empty plain scalar, which is a null, stays plain: as a mapping
  key, it is written as an explicit key (`? `), because `''` would read back as an empty string.
- A tag is written with the handle of a `%TAG` directive of the document whose prefix starts it, such as `!!str` for
  `tag:yaml.org,2002:str`, else in the verbatim form `!<...>`. Only a local tag (`!point`) and a URI
  (`tag:example.com,2000:point`) have a verbatim form: the emitter throws a
  [YamlException](cref:Meziantou.Framework.Yaml.YamlException) for another tag, such as `y-light`, that no directive starts.
- A [node](05-DocumentObjectModel.md) writes its events to an emitter with [WriteTo](cref:Meziantou.Framework.Yaml.Nodes.YamlNode.WriteTo), and lists them with
  [EnumerateEvents](cref:Meziantou.Framework.Yaml.Nodes.YamlNode.EnumerateEvents).
