---
title: Features and geometries
description: Build features in memory, and compute bounds, label positions, distances and point-in-polygon tests.
---

## Features

A [GeoFeature](cref:Meziantou.Framework.Geo.GeoFeature) is a thing on a map, such as a country, a road or a city. It has:

- a [Geometry](cref:Meziantou.Framework.Geo.GeoFeature.Geometry), or `null` for a feature without a location;
- [Properties](cref:Meziantou.Framework.Geo.GeoFeature.Properties): a dictionary of values, such as a name or a
  population;
- an [Id](cref:Meziantou.Framework.Geo.GeoFeature.Id), or `null`.

A [GeoFeatureCollection](cref:Meziantou.Framework.Geo.GeoFeatureCollection) is a list of features, with an optional
[Name](cref:Meziantou.Framework.Geo.GeoFeatureCollection.Name). Build one with a collection initializer:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=Geo_BuildFeatures;format=md-fenced-code-block;dedent=true -->
```csharp
var cities = new GeoFeatureCollection
{
    new GeoFeature(
        new GeoPoint(new GeoPosition(Longitude: 2.35, Latitude: 48.86)),
        new Dictionary<string, object?> { ["name"] = "Paris", ["population"] = 2_100_000 },
        id: "paris"),
    new GeoFeature(
        new GeoPoint(new GeoPosition(Longitude: -0.13, Latitude: 51.51)),
        new Dictionary<string, object?> { ["name"] = "London", ["population"] = 8_900_000 },
        id: "london"),
};

var json = GeoJson.Format(cities);
Assert.StartsWith("""{"type":"FeatureCollection","features":[{"type":"Feature","id":"paris",""", json);
```
<!-- endref -->

The readers give the values of the properties as a `string`, a `double`, a `bool` or `null`; each format documents
the other types it gives, such as dates. The writers also accept the other numeric types and the dates and times.

## Geometries

A [GeoPosition](cref:Meziantou.Framework.Geo.GeoPosition) is a longitude and a latitude, in degrees, with an optional
altitude in meters. The geometries are the seven types of GeoJSON and of the Simple Features of the OGC:

| Type | Use it for |
|---|---|
| [GeoPoint](cref:Meziantou.Framework.Geo.GeoPoint) | a place, such as a city |
| [GeoMultiPoint](cref:Meziantou.Framework.Geo.GeoMultiPoint) | several places |
| [GeoLineString](cref:Meziantou.Framework.Geo.GeoLineString) | a line, such as a river or a route |
| [GeoMultiLineString](cref:Meziantou.Framework.Geo.GeoMultiLineString) | several lines, such as the segments of a track |
| [GeoPolygon](cref:Meziantou.Framework.Geo.GeoPolygon) | an area: an exterior ring, then the rings of its holes |
| [GeoMultiPolygon](cref:Meziantou.Framework.Geo.GeoMultiPolygon) | several areas, such as a country and its islands |
| [GeoGeometryCollection](cref:Meziantou.Framework.Geo.GeoGeometryCollection) | geometries of different types |

The geometries are immutable. [Type](cref:Meziantou.Framework.Geo.GeoGeometry.Type) tells the type of a geometry, and
pattern matching gives its members:

- [GetPositions](cref:Meziantou.Framework.Geo.GeoGeometry.GetPositions) returns every position, in order.
- [GetSimpleGeometries](cref:Meziantou.Framework.Geo.GeoGeometry.GetSimpleGeometries) returns the points, lines and
  polygons of a multi-geometry or a collection.
- [GetBounds](cref:Meziantou.Framework.Geo.GeoGeometry.GetBounds) returns the box of longitudes and latitudes that
  contains the geometry, and [GeoFeatureCollection.GetBounds](cref:Meziantou.Framework.Geo.GeoFeatureCollection.GetBounds)
  the box of a whole map.
- [LabelPosition](cref:Meziantou.Framework.Geo.GeoGeometry.LabelPosition) is a position to label the geometry: the
  centroid of its largest polygon, so the islands of a country do not pull the label into the sea.

## Measures and times

A position can also have a [Measure](cref:Meziantou.Framework.Geo.GeoPosition.Measure), the M value of WKT, WKB,
shapefiles and FlatGeobuf, such as the distance from the start of a road, and a
[Time](cref:Meziantou.Framework.Geo.GeoPosition.Time), such as the time of a point of a GPS track. Both are optional:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=Geo_PositionMeasureAndTime;format=md-fenced-code-block;dedent=true -->
```csharp
// The measure and the time are optional, set with an object initializer
var position = new GeoPosition(Longitude: 2.35, Latitude: 48.86, Altitude: 35)
{
    Measure = 1250,
    Time = new DateTimeOffset(2024, 9, 1, 7, 0, 0, TimeSpan.Zero),
};

// A with expression keeps the values it does not set
var moved = position with { Longitude = 2.36 };
Assert.Equal(1250, moved.Measure);
Assert.Equal(position.Time, moved.Time);
Assert.Null(new GeoPosition(2.35, 48.86).Time);
```
<!-- endref -->

The formats read and write the values they can hold:

| Format | Measures | Times |
|---|---|---|
| [GPX](04-Formats/06-Gpx.md) | no | the `time` of the points, in UTC |
| [KML](04-Formats/05-KmlAndKmz.md) | no | the `when` of the `gx:Track` elements |
| [WKT and WKB](04-Formats/07-WktAndWkb.md) | the `M` and `ZM` geometries | no |
| [Shapefile](04-Formats/04-Shapefile.md) | the M values of the M and Z shapes | no |
| [FlatGeobuf](04-Formats/09-FlatGeobuf.md) | the M values | no |
| GeoJSON, TopoJSON, encoded polylines | no | no |

Two positions are equal when their coordinates, altitudes and measures are equal and their times are the same instant.
[Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip) interpolates the measure and the time of the positions it adds
where a line leaves the box, and [Simplify](cref:Meziantou.Framework.Geo.GeoGeometry.Simplify) keeps the values of the
positions it keeps. The positions of the polygons that the repair and the boolean operations return have no altitude,
measure or time.

## Distances

[GetDistance](cref:Meziantou.Framework.Geo.GeoPosition.GetDistance) returns the great-circle distance between two
positions, in meters, and [GetLength](cref:Meziantou.Framework.Geo.GeoLineString.GetLength) the length of a line:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=Geo_Distance;format=md-fenced-code-block;dedent=true -->
```csharp
var paris = new GeoPosition(Longitude: 2.35, Latitude: 48.86);
var london = new GeoPosition(Longitude: -0.13, Latitude: 51.51);

// The great-circle distance, in meters
Assert.Equal(343.5, paris.GetDistance(london) / 1000, 1);
Assert.Equal(343.5, new GeoLineString([paris, london]).GetLength() / 1000, 1);
```
<!-- endref -->

The distance is computed on a sphere with the mean radius of the Earth: it can differ by up to 0.5% from the distance
on the ellipsoid that GPS devices use. The altitudes are ignored.
[Distance](cref:Meziantou.Framework.Geo.GeoGeometry.Distance) measures the distance between two geometries on the
ellipsoid: see [geometry operations](06-GeometryOperations.md).

## Point in polygon

[GeoPolygon.Contains](cref:Meziantou.Framework.Geo.GeoPolygon.Contains) and
[GeoMultiPolygon.Contains](cref:Meziantou.Framework.Geo.GeoMultiPolygon.Contains) tell whether a position is inside an
area and outside its holes, for instance to find the country of a position:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=Geo_Contains;format=md-fenced-code-block;dedent=true -->
```csharp
// A square with a square hole
var polygon = new GeoPolygon(
[
    [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)],
    [new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4)],
]);

Assert.True(polygon.Contains(new GeoPosition(Longitude: 2, Latitude: 2)));
Assert.False(polygon.Contains(new GeoPosition(Longitude: 5, Latitude: 5))); // in the hole
Assert.False(polygon.Contains(new GeoPosition(Longitude: 12, Latitude: 5)));
```
<!-- endref -->

The test treats the edges as straight lines in degrees, as on an equirectangular map, not as great circles. The
difference matters only for long edges far from the equator. The result is unspecified for a position on an edge.
