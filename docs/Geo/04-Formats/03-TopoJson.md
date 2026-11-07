---
title: TopoJSON
description: Read the objects of a TopoJSON topology, such as the world-atlas maps, and write compact topologies with shared borders.
---

[TopoJSON](https://github.com/topojson/topojson-specification) stores the borders that regions share once, and can
round the positions to a grid: a world map is much smaller than in GeoJSON. Maps such as `world-atlas` and `us-atlas`
are published in this format.

## Read a topology

A topology holds named objects, such as `countries` and `land`. Read one with
[TopoJson.Read](cref:Meziantou.Framework.Geo.TopoJson.Read) or [TopoJson.Parse](cref:Meziantou.Framework.Geo.TopoJson.Parse)
and its name:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/TopoJsonTests.cs;region=Geo_TopoJsonRead;format=md-fenced-code-block;dedent=true -->
```csharp
// A file of world-atlas: its "countries" object is a collection of polygons that share their borders
GeoFeatureCollection countries;
using (var stream = File.OpenRead("countries-110m.json"))
{
    countries = TopoJson.Read(stream, "countries");
}

var france = countries.Single(country => country.Id == "250");
Assert.Equal("France", france.Properties["name"]);
Assert.True(Assert.IsType<GeoPolygon>(france.Geometry).Contains(new GeoPosition(-1.5, 43)));
```
<!-- endref -->

- Without a name, `Read` and `Parse` return the features of all the objects.
  [ReadObjects](cref:Meziantou.Framework.Geo.TopoJson.ReadObjects) and
  [ParseObjects](cref:Meziantou.Framework.Geo.TopoJson.ParseObjects) return each object as a collection, by name.
- An object that is a `GeometryCollection` gives a feature for each of its geometries, with their `id` and
  `properties`. The values of the properties are read as in [GeoJSON](02-GeoJson.md).
- The reader decodes the quantized positions, and keeps a third value of a position as the altitude.

## Write a topology

[TopoJson.Write](cref:Meziantou.Framework.Geo.TopoJson.Write) and [TopoJson.Format](cref:Meziantou.Framework.Geo.TopoJson.Format)
write a collection as an object named after its [Name](cref:Meziantou.Framework.Geo.GeoFeatureCollection.Name)
(`features` when it has none), and [WriteObjects](cref:Meziantou.Framework.Geo.TopoJson.WriteObjects) and
[FormatObjects](cref:Meziantou.Framework.Geo.TopoJson.FormatObjects) write several objects:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/TopoJsonTests.cs;region=Geo_TopoJsonWrite;format=md-fenced-code-block;dedent=true -->
```csharp
// The border of France and Spain is written once, and the positions are rounded to a grid of 10,000x10,000
var options = new TopoJsonWriterOptions { Quantization = 10_000 };
using (var stream = File.Create("countries.topojson"))
{
    TopoJson.Write(countries, stream, options);
}

var copy = TopoJson.Parse(File.ReadAllText("countries.topojson"), "countries");
Assert.Equal(["France", "Spain"], copy.Select(country => country.Properties["name"]));
```
<!-- endref -->

- The writer finds the borders that regions share, as `topojson-server` does, and writes each of them once.
- Without [Quantization](cref:Meziantou.Framework.Geo.TopoJsonWriterOptions.Quantization), the positions are written
  as they are, and read back identical. With it, they are rounded to a grid of that many values along each axis,
  which makes the file smaller: 10,000 is precise enough for a world map drawn on a screen.
- The altitudes are written as a third value of the positions. The specification allows it, but GDAL does not read
  them.
