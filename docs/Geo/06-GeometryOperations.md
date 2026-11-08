---
title: Geometry operations
description: Repair, clip and simplify geometries, split them along the 180th meridian, combine polygons, and convert positions from another coordinate system.
---

The operations are methods of [GeoGeometry](cref:Meziantou.Framework.Geo.GeoGeometry). They return new geometries:
the geometries are immutable.

## Repair polygons

The polygons of real data are often invalid: a ring that crosses itself after a simplification, a hole that sticks out
of its polygon, rings in the wrong direction. Some renderers and tools draw such polygons wrong or reject them.
[MakeValid](cref:Meziantou.Framework.Geo.GeoGeometry.MakeValid) repairs them:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_MakeValid;format=md-fenced-code-block;dedent=true -->
```csharp
// A ring that crosses itself: a bow-tie
var bowTie = new GeoPolygon([[new(0, 0), new(2, 2), new(2, 0), new(0, 2), new(0, 0)]]);

// It becomes its two triangles, whose exterior rings turn counter-clockwise
var repaired = Assert.IsType<GeoMultiPolygon>(bowTie.MakeValid());
Assert.HasCount(2, repaired.Polygons);
```
<!-- endref -->

- A polygon becomes the area its rings enclose an odd number of times: the two loops of a bow-tie are kept, and the
  part of a hole that is outside its polygon becomes a part of the area. The result is a
  [GeoPolygon](cref:Meziantou.Framework.Geo.GeoPolygon), or a [GeoMultiPolygon](cref:Meziantou.Framework.Geo.GeoMultiPolygon)
  when the polygon is cut in several parts.
- A multi-polygon becomes the union of the areas of its polygons: the parts that overlap are merged.
- The exterior rings turn counter-clockwise and the holes clockwise, as [RFC 7946](https://www.rfc-editor.org/rfc/rfc7946#section-3.1.6)
  wants them, and every ring ends with its first position.
- The lines lose their consecutive repeated positions. The points are unchanged.

## Clip to a box

[Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip) keeps the part of a geometry that is inside a
[GeoBounds](cref:Meziantou.Framework.Geo.GeoBounds), for instance to cut the land polygons of the whole world to the
map of a country:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_Clip;format=md-fenced-code-block;dedent=true -->
```csharp
var square = new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)]]);
var bounds = new GeoBounds(West: 5, South: -5, East: 15, North: 5);

// The quarter of the square that is inside the box
var clipped = Assert.IsType<GeoPolygon>(square.Clip(bounds));
Assert.Equal(new GeoBounds(5, 0, 10, 5), clipped.GetBounds());

// A line that leaves the box and comes back is cut in two
var line = new GeoLineString([new(6, 1), new(20, 1), new(20, 2), new(6, 2)]);
var pieces = Assert.IsType<GeoMultiLineString>(line.Clip(bounds));
Assert.HasCount(2, pieces.LineStrings);
```
<!-- endref -->

It returns `null` when nothing is inside the box, and the geometry itself when all of it is. The points outside the box
are dropped, the lines are cut where they cross its edges, and the polygons are cut as by
[Intersection](cref:Meziantou.Framework.Geo.GeoGeometry.Intersection). A box whose west side is east of its east side
crosses the 180th meridian: the result has the part of the geometry on each side.

## Split along the 180th meridian

[SplitAtAntimeridian](cref:Meziantou.Framework.Geo.GeoGeometry.SplitAtAntimeridian) cuts the lines and the polygons
that cross the 180th meridian into a part on each side, as [RFC 7946](https://www.rfc-editor.org/rfc/rfc7946#section-3.1.9)
wants it. The edges whose ends are more than 180° of longitude apart, such as an edge from 177 to -178, are taken the short
way, across the meridian:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_SplitAtAntimeridian;format=md-fenced-code-block;dedent=true -->
```csharp
// Fiji, as a ring whose edges cross the 180th meridian
var fiji = new GeoPolygon([[new(177, -19), new(-178, -19), new(-178, -16), new(177, -16), new(177, -19)]]);
Assert.True(fiji.CrossesAntimeridian);

// Split along the 180th meridian, as RFC 7946 wants it: a polygon on each side
var split = Assert.IsType<GeoMultiPolygon>(fiji.SplitAtAntimeridian());
Assert.Equal(new GeoBounds(177, -19, 180, -16), split.Polygons[0].GetBounds());
Assert.Equal(new GeoBounds(-180, -19, -178, -16), split.Polygons[1].GetBounds());
Assert.True(split.Contains(new GeoPosition(Longitude: 179, Latitude: -17)));
Assert.False(split.CrossesAntimeridian);

// The box of the parts crosses the 180th meridian: its west side is east of its east side
var bounds = split.GetBounds(GeoEdgeType.Geodesic)!.Value;
Assert.True(bounds.CrossesAntimeridian());
Assert.Equal((177, -178), (bounds.West, bounds.East));
Assert.True(bounds.Contains(new GeoPosition(Longitude: -179, Latitude: -17)));
```
<!-- endref -->

- A line becomes a [GeoMultiLineString](cref:Meziantou.Framework.Geo.GeoMultiLineString) whose parts end and start on
  the meridian, at the longitudes 180 and -180. The position where it crosses is interpolated in degrees.
- A polygon becomes a [GeoMultiPolygon](cref:Meziantou.Framework.Geo.GeoMultiPolygon) with a polygon on each side. A
  ring that goes around a pole, such as the coast of Antarctica, is closed through the pole it is nearest to.
- The longitudes greater than 180 or smaller than -180 go around the world, back to [-180, 180]. The points are moved,
  not split.

The geometries with no edge to split and no longitude to move are returned as they are. The edges whose ends are both on
the meridian, such as the edges of a box around the whole world from -180 to 180, are not split. The parts of the
polygons are cut as by [Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip): see [Precision](#precision).

## Simplify

[Simplify](cref:Meziantou.Framework.Geo.GeoGeometry.Simplify) removes the positions that a map does not need, with the
Douglas–Peucker algorithm: a position closer than the tolerance to the line between the positions kept around it is
removed. The tolerance is in degrees; 0.001° is about 100 meters.

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_Simplify;format=md-fenced-code-block;dedent=true -->
```csharp
// A line with a small zigzag, about 100 meters wide
var line = new GeoLineString([new(0, 0), new(1, 0.0005), new(2, -0.0005), new(3, 0)]);

// The positions closer than 0.001° to the simplified line are removed
var simplified = Assert.IsType<GeoLineString>(line.Simplify(tolerance: 0.001));
Assert.Equal([new GeoPosition(0, 0), new(3, 0)], simplified.Positions);
```
<!-- endref -->

The rings smaller than the tolerance disappear, and the method returns `null` when every line and ring does. Removing
positions can make a ring cross another one: the simplified polygons are repaired with
[MakeValid](cref:Meziantou.Framework.Geo.GeoGeometry.MakeValid).

## Combine polygons

[Union](cref:Meziantou.Framework.Geo.GeoGeometry.Union),
[Intersection](cref:Meziantou.Framework.Geo.GeoGeometry.Intersection) and
[Difference](cref:Meziantou.Framework.Geo.GeoGeometry.Difference) combine the areas of two polygons, multi-polygons or
collections of polygons:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_BooleanOperations;format=md-fenced-code-block;dedent=true -->
```csharp
var a = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)]]);
var b = new GeoPolygon([[new(2, 2), new(6, 2), new(6, 6), new(2, 6), new(2, 2)]]);

var union = a.Union(b);               // an area of 28 square degrees
var intersection = a.Intersection(b); // the square from (2, 2) to (4, 4)
var difference = a.Difference(b);     // a with a corner cut away
```
<!-- endref -->

They return `null` when the result is empty. The exterior ring of each polygon adds its area and its holes remove
theirs, whatever the direction of the rings: repair polygons whose rings cross with
[MakeValid](cref:Meziantou.Framework.Geo.GeoGeometry.MakeValid) first.

## Convert positions

[Transform](cref:Meziantou.Framework.Geo.GeoGeometry.Transform) applies a function to every position, and keeps the
type of the geometry. Use it to convert data that is not in longitudes and latitudes, such as the shapefiles of
[OpenStreetMap Data](https://osmdata.openstreetmap.de/) in Web Mercator (EPSG:3857), whose coordinates are meters:
[MercatorProjection](cref:Meziantou.Framework.Geo.MercatorProjection) converts them back once they are divided by the
radius of that projection, 6,378,137 meters.

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_Transform;format=md-fenced-code-block;dedent=true -->
```csharp
// A point in Web Mercator (EPSG:3857), in meters, converted to a longitude and a latitude
var mercator = new GeoPoint(new GeoPosition(261_600, 6_250_600));
var projection = new MercatorProjection();
var point = (GeoPoint)mercator.Transform(position => projection.Invert(position.Longitude / 6_378_137, position.Latitude / 6_378_137)!.Value);
```
<!-- endref -->

[GetBoundaryLines](cref:Meziantou.Framework.Geo.GeoGeometry.GetBoundaryLines) returns the rings of the polygons as
closed lines, for instance the coastlines of land polygons.

## Precision

The operations on polygons are exact on a grid of ten-millionths of a degree, about a centimeter: the positions of the
polygons they return are on that grid, and lose their altitude, measure and time. An edge that passes within about a unit and a half of a
vertex is cut at that vertex, so a part only a few units wide, a few centimeters, can move by a few units or
disappear. The edges are straight lines in degrees, as on an equirectangular map: split the shapes that cross the 180th
meridian with [SplitAtAntimeridian](cref:Meziantou.Framework.Geo.GeoGeometry.SplitAtAntimeridian) first. The coordinates of the polygons must be between -100,000 and 100,000: convert projected
coordinates to degrees with [Transform](cref:Meziantou.Framework.Geo.GeoGeometry.Transform) first.
