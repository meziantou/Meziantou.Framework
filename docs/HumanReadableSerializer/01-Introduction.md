---
title: Introduction
description: What the human-readable serializer does, and when to use it.
---

`Meziantou.Framework.HumanReadableSerializer` writes any .NET object as readable, indented text, in a YAML-like format:
one `Name: value` line per property, nested objects indented below their name, and one `- item` line per item of a
collection. [Getting started](02-GettingStarted.md) shows an example.

It is a one-way serializer: there is no deserializer. The text is made for people and for comparisons, so it is the
same on every machine: numbers and dates are written with the invariant culture, and lines are separated by `\n` on
every platform.

## When to use it

- **Snapshot tests.** `Meziantou.Framework.SnapshotTesting` and `Meziantou.Framework.InlineSnapshotTesting` use it to
  write the value under test. See [Snapshot testing](09-SnapshotTesting.md).
- **Logs and diagnostics**, to dump an object, an exception or an HTTP response in a form that is easy to read.
- **Diffs**, to compare two objects line by line.

## What you can do with it

- **Serialize any object**: anonymous types, records, collections, dictionaries, enums, dates, JSON and XML documents,
  HTTP messages, and many .NET types. See [Getting started](02-GettingStarted.md) and
  [Output format](03-OutputFormat.md).
- **Choose, rename and order the members** with attributes or options, including on types you do not own. See
  [Members](04-Members.md).
- **Write a type your way** with a converter. See [Converters](05-Converters.md).
- **Format JSON, XML, HTML and form values** embedded in the object. See
  [JSON, XML and HTML values](06-JsonXmlAndHtml.md).
- **Write HTTP requests and responses** with their headers and content. See [HTTP messages](07-HttpMessages.md).

## Packages and namespaces

| Package | Content |
| --- | --- |
| `Meziantou.Framework.HumanReadableSerializer` | The serializer, its options and attributes, and the JSON, XML and URL-encoded form formatters |
| `Meziantou.Framework.HumanReadableSerializer.Html` | A formatter that normalizes HTML values |

The serializer, its options, the attributes and the converter base classes are in the [Meziantou.Framework.HumanReadable](cref:Meziantou.Framework.HumanReadable)
namespace. The formatters are in [Meziantou.Framework.HumanReadable.ValueFormatters](cref:Meziantou.Framework.HumanReadable.ValueFormatters), and the HTTP options in
[Meziantou.Framework.HumanReadable.Converters](cref:Meziantou.Framework.HumanReadable.Converters).
