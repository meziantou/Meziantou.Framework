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
