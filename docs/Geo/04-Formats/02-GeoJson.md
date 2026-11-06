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
