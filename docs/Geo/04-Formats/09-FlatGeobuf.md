---
title: FlatGeobuf
description: Read and write FlatGeobuf files, a compact binary format with a spatial index, and read only the features of a region.
---

[FlatGeobuf](https://flatgeobuf.org/) is a binary format of features: smaller and faster to read than GeoJSON, and read
by GDAL, QGIS, OpenLayers and Leaflet. Its spatial index lets a reader get the features of a region without decoding
the others, which suits large datasets such as the borders of every country.

## Read and write

[FlatGeobuf.Read](cref:Meziantou.Framework.Geo.FlatGeobuf.Read) reads a file, a stream or bytes, and
[FlatGeobuf.Write](cref:Meziantou.Framework.Geo.FlatGeobuf.Write) writes them:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/FlatGeobufTests.cs;region=Geo_FlatGeobufReadWrite;format=md-fenced-code-block;dedent=true -->
```csharp
var features = new GeoFeatureCollection
{
    new GeoFeature(
        new GeoPoint(new GeoPosition(Longitude: 2.35, Latitude: 48.86)),
        new Dictionary<string, object?> { ["name"] = "Paris", ["population"] = 2_100_000 }),
    new GeoFeature(
        new GeoPoint(new GeoPosition(Longitude: -0.13, Latitude: 51.51)),
        new Dictionary<string, object?> { ["name"] = "London", ["population"] = 8_900_000 }),
};
features.Name = "cities";

var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".fgb");
FlatGeobuf.Write(features, path);
var cities = FlatGeobuf.Read(path);
Assert.Equal("cities", cities.Name);
Assert.HasCount(2, cities);
```
<!-- endref -->

## Read the features of a region

Set [FlatGeobufReaderOptions.Bounds](cref:Meziantou.Framework.Geo.FlatGeobufReaderOptions.Bounds) to read only the
features whose bounds intersect a box. With the spatial index, the other features are not decoded:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/FlatGeobufTests.cs;region=Geo_FlatGeobufReadBounds;format=md-fenced-code-block;dedent=true -->
```csharp
// With a spatial index, only the features whose bounds intersect the box are decoded
var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
var features = FlatGeobuf.Read("TestData/flatgeobuf/countries.fgb", new FlatGeobufReaderOptions { Bounds = europe });
Assert.Contains("France", features.Select(feature => (string?)feature.Properties["name"]));
Assert.DoesNotContain("Japan", features.Select(feature => (string?)feature.Properties["name"]));
```
<!-- endref -->

The features are kept whole: a country that crosses the edge of the box is not cut. Cut them with
[Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip), see [geometry operations](../06-GeometryOperations.md).

## The spatial index

The writer adds the index by default. The index requires the features in its own order, along a Hilbert curve, so that
the features that are close on the map are close in the file: the features are not written in the order of the
collection. Set [IncludeSpatialIndex](cref:Meziantou.Framework.Geo.FlatGeobufWriterOptions.IncludeSpatialIndex) to
`false` to keep the order. A collection with a feature without geometry is written without index.
[IndexNodeSize](cref:Meziantou.Framework.Geo.FlatGeobufWriterOptions.IndexNodeSize) sets the number of children of each
node of the index, 16 by default.

## What is kept

- **Geometries**: points, lines, polygons, their multi-geometries and geometry collections, with their altitudes. A
  file with an altitude for one position gets one for every position: the others get 0. The curves, surfaces and
  triangles of other writers throw a `FormatException`; the M and time values are dropped.
- **Properties**: each column has a type, given by the values of the property: integers, numbers, booleans, strings,
  dates and times (to the millisecond), arrays of bytes, and `JsonElement` objects and arrays as JSON. A property whose
  values have different types is written as the most general type that holds them: a `long`, a `double`, or else the
  text of the values. The reader returns the .NET type of the column, such as an `int`, a `long` or a `DateTimeOffset`.
- **Not kept**: the identifiers of the features, and the coordinate system. The writer declares longitudes and latitudes
  (EPSG:4326), and the reader does not convert the coordinates of a file in another system.
