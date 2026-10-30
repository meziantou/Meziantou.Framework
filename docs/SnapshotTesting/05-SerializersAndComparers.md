---
title: Serializers and comparers
description: Configure how values are written to snapshot files and how files are compared, and write your own.
---

An assertion runs in two steps. The *serializers* convert the value to the content of one or more snapshot files, then
the *comparers* decide whether each file matches its verified file. Both are configured on [SnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings).

## The default serializers

[SnapshotSettings.Serializers](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Serializers) starts with three serializers:

- **Objects** are written as readable, YAML-like text by the human-readable serializer
  ([`Meziantou.Framework.HumanReadableSerializer`](https://www.nuget.org/packages/Meziantou.Framework.HumanReadableSerializer)).
  Properties with a default value (`null`, `0`, `false`) are omitted and obsolete members are skipped. JSON and XML
  values are indented, the attributes of XML elements are ordered, and the converters for HTTP messages and their
  headers are registered.
- **`byte[]`** values are stored as their bytes.
- **`Stream`** values are stored as their bytes, read from the current position of the stream to its end.

## Configure how objects are written

[ConfigureHumanReadableSerializer](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsHumanReadableSerializerExtensions.ConfigureHumanReadableSerializer) changes the options of the human-readable serializer of a settings instance:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_ConfigureSerializer;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new { Name = "John", Email = (string?)null, Age = 42 };

var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options =>
{
    options.PropertyOrder = StringComparer.Ordinal; // Sort the properties by name
    options.DefaultIgnoreCondition = HumanReadableIgnoreCondition.Never; // Also write null and default values
});

Snapshot.Validate(value, settings);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/__snapshots__/SnapshotTests_DocumentationExample_ConfigureSerializer.verified.txt;format=md-fenced-code-block;language=text -->
```text
Age: 42
Email: <null>
Name: John
```
<!-- endref -->

The options are those of [HumanReadableSerializerOptions](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions): order properties and dictionary keys, ignore members by name,
by type or when they throw, format JSON, XML and URL-encoded values (and HTML, with the
`Meziantou.Framework.HumanReadableSerializer.Html` package), show invisible characters, and scrub values.
The [inline snapshot serializer page](../InlineSnapshotTesting/04-Serializers.md) shows more of them; they work the
same way here. [AddConverter](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsHumanReadableSerializerExtensions.AddConverter) adds a [HumanReadableConverter](cref:Meziantou.Framework.HumanReadable.HumanReadableConverter) that writes a type your way.

[ConfigureHumanReadableSerializer](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettingsHumanReadableSerializerExtensions.ConfigureHumanReadableSerializer) only affects the settings instance it is called on. Call it on a copy
(`SnapshotSettings.Default with { }`) for one test, or on [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) in a
[module initializer](04-Settings.md#configure-every-test) for every test.

## Write your own serializer

Implement [ISnapshotSerializer](cref:Meziantou.Framework.SnapshotTesting.ISnapshotSerializer) to store a type in its own format. [TrySerialize](cref:Meziantou.Framework.SnapshotTesting.ISnapshotSerializer.TrySerialize) receives the requested snapshot type
and the value, and returns `false` for the values it does not handle:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_CustomSerializer;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class CsvSnapshotSerializer : ISnapshotSerializer
{
    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (value is not string[][] rows)
        {
            result = null;
            return false; // Let the serializers registered before this one handle the value
        }

        var csv = string.Join('\n', rows.Select(row => string.Join(',', row)));
        result = new SerializedSnapshot([new SnapshotData("csv", Encoding.UTF8.GetBytes(csv))]);
        return true;
    }
}
```
<!-- endref -->

- A [SerializedSnapshot](cref:Meziantou.Framework.SnapshotTesting.SerializedSnapshot) is a list of [SnapshotData](cref:Meziantou.Framework.SnapshotTesting.SnapshotData), each one the extension and the bytes of a file. A serializer that
  returns several files produces `Name_0`, `Name_1`, ... (see [File names](03-WritingTests/03-SnapshotFiles.md#file-names)).
- The serializers are tried from the last one added to the first: a serializer you add takes precedence over the
  built-in ones. The human-readable serializer, the first one, accepts every value.
- The extension of the data, not the requested type, gives the extension of the file. The serializers of the
  extension packages use it to store a PDF document as PNG pages, or a generator result as `.cs` files.

## The default comparers

| Format | Comparison |
| --- | --- |
| [SnapshotType.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Default) (`txt`), [Svg](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Svg), and every known text format (`json`, `yaml`, `xml`, `html`, `cs`, `csv`, ...) | The text, without regard to line endings and a leading UTF-8 byte order mark |
| Any other format (`png`, `pdf`, `bin`, ...) | The bytes |

A text format without its own comparer uses the comparer registered for [SnapshotType.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Default), and any other format
uses the one registered for [SnapshotType.None](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.None).

## Write your own comparer

Implement [ISnapshotComparer](cref:Meziantou.Framework.SnapshotTesting.ISnapshotComparer) and register it for a snapshot type with `Comparers.Set`. Implement
`Equals(expected, actual, out mismatchReason)` to explain a mismatch: the reason is shown under the changed file in the
failure message.

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_CustomComparer;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class UnorderedLinesSnapshotComparer : ISnapshotComparer
{
    public bool Equals(SnapshotData expected, SnapshotData actual) => Equals(expected, actual, out _);

    public bool Equals(SnapshotData expected, SnapshotData actual, out string? mismatchReason)
    {
        var expectedLines = Encoding.UTF8.GetString(expected.Data).Split('\n').Order(StringComparer.Ordinal);
        var actualLines = Encoding.UTF8.GetString(actual.Data).Split('\n').Order(StringComparer.Ordinal);
        if (expectedLines.SequenceEqual(actualLines, StringComparer.Ordinal))
        {
            mismatchReason = null;
            return true;
        }

        mismatchReason = "The files do not contain the same lines";
        return false;
    }
}
```
<!-- endref -->

Register the serializer and the comparer, then validate:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Tests/SnapshotTests.cs;region=Snapshot_CustomSerializerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with { };
settings.Serializers.Add(new CsvSnapshotSerializer());
settings.Comparers.Set(SnapshotType.Create("csv"), new UnorderedLinesSnapshotComparer());

string[][] rows = [["Name", "Age"], ["John", "42"], ["Jane", "36"]];
Snapshot.Validate(rows, settings); // __snapshots__/SnapshotTests_DocumentationExample_CustomSerializerAndComparer.verified.csv
```
<!-- endref -->

With this comparer, the verified CSV file may list the rows in any order.

A comparer is chosen for each file. When a serializer stores another format than the requested one (an animated GIF
stored as PNG frames, a source generator result stored as C# files), the comparer registered for the stored format wins,
and the comparer of the requested type is the fallback. `Comparers.Get(type)` returns the comparer used for a type, and
`Comparers.Remove(type)` removes a registration.

## Comparers for images and PDF documents

Images are compared byte for byte by default, so a re-encoded image with the same pixels does not match. The extension
packages register comparers that look at the content:

- [`Meziantou.Framework.SnapshotTesting.Imaging`](07-Extensions/02-Images.md) compares images by their pixels, exactly
  or with similarity thresholds.
- [`Meziantou.Framework.SnapshotTesting.Pdf`](07-Extensions/03-Pdf.md) renders each page of a PDF document to an image,
  compared by its pixels, and stores the metadata as JSON.
