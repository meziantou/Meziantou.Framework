---
title: GeoJSON
description: Read and write GeoJSON documents, and choose how the writer indents them and adds bounding boxes.
---

[GeoJSON](https://datatracker.ietf.org/doc/html/rfc7946) is the JSON format of web maps.
[GeoJson.Read](cref:Meziantou.Framework.Geo.GeoJson.Read) and [GeoJson.Parse](cref:Meziantou.Framework.Geo.GeoJson.Parse)
read a `FeatureCollection`, a `Feature` or a geometry, and [GeoJson.Write](cref:Meziantou.Framework.Geo.GeoJson.Write)
and [GeoJson.Format](cref:Meziantou.Framework.Geo.GeoJson.Format) write a `FeatureCollection`:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/GeoJsonTests.cs;region=Geo_GeoJsonReadWrite;format=md-fenced-code-block;dedent=true -->
```csharp
GeoFeatureCollection countries;
using (var stream = File.OpenRead("countries.geojson"))
{
    countries = GeoJson.Read(stream);
}

// A JSON number is a double, and a string is a string
var large = countries.Where(country => (double)country.Properties["population"]! > 50).ToList();
Assert.Equal("France", Assert.Single(large).Properties["name"]);

using (var stream = File.Create("large-countries.geojson"))
{
    GeoJson.Write(new GeoFeatureCollection(large), stream, new GeoJsonWriterOptions { Indented = true });
}
```
<!-- endref -->

[ParseGeometry](cref:Meziantou.Framework.Geo.GeoJson.ParseGeometry) and
[FormatGeometry](cref:Meziantou.Framework.Geo.GeoJson.FormatGeometry) read and write a single geometry.

## Read a large document

[GeoJson.ReadFeaturesAsync](cref:Meziantou.Framework.Geo.GeoJson.ReadFeaturesAsync) returns the features one at a time,
as it reads the stream: only the feature being read is in memory, so an export of millions of features can be filtered
or converted with bounded memory:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/GeoJsonTests.cs;region=Geo_GeoJsonReadFeaturesAsync;format=md-fenced-code-block;dedent=true -->
```csharp
// The features are read one at a time: a large export is processed without loading it in memory
await using var stream = File.OpenRead(path);
var points = 0;
await foreach (var feature in GeoJson.ReadFeaturesAsync(stream))
{
    if (feature.Geometry is GeoPoint)
    {
        points++;
    }
}

Assert.Equal(1, points);
```
<!-- endref -->

- The members of the `FeatureCollection` can be in any order. The features are returned as soon as the `features`
  array starts, and a `type` written after the array that is not `FeatureCollection` throws a `FormatException` once
  they are returned.
- The `name` of the collection is not returned: use [Read](cref:Meziantou.Framework.Geo.GeoJson.Read) to get it.
- A `Feature` or a geometry is a single feature: it is read whole.
- Invalid data throws a `FormatException` when the reader reaches it: the features before it are returned first.

## What the reader keeps

- The seven geometry types, with their altitudes.
- The `name` of a `FeatureCollection`, as [Name](cref:Meziantou.Framework.Geo.GeoFeatureCollection.Name). The `bbox`
  and the other members that GeoJSON does not define are ignored.
- The properties: a JSON string is a `string`, a number a `double`, `true` and `false` a `bool`, `null` is `null`, and
  an object or an array is a `JsonElement`.
- The `id`: a string, or the JSON text of a number (`42`).

## Options of the writer

[GeoJsonWriterOptions](cref:Meziantou.Framework.Geo.GeoJsonWriterOptions) sets:

- [Indented](cref:Meziantou.Framework.Geo.GeoJsonWriterOptions.Indented): indent the JSON.
- [WriteBoundingBoxes](cref:Meziantou.Framework.Geo.GeoJsonWriterOptions.WriteBoundingBoxes): add a `bbox` to the
  collection, the features and the geometries.

The writer writes the identifiers as strings, the numbers as JSON numbers (`null` when they are not finite), a
`JsonElement` as it is, a dictionary as an object, a collection as an array, and the other values, such as dates, as
their text. Only the characters JSON requires are escaped, so names in other scripts stay readable.
