---
title: Geometry operations
description: Repair, clip and simplify geometries, combine polygons, measure distances, compute buffers and convex hulls, and convert positions from another coordinate system.
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
[Intersection](cref:Meziantou.Framework.Geo.GeoGeometry.Intersection).

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

## Measure distances

[Distance](cref:Meziantou.Framework.Geo.GeoGeometry.Distance) returns the shortest distance between two geometries of
any type, in meters, for instance to find the nearest road to a position or how far a GPS track passes from a zone:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_Distance;format=md-fenced-code-block;dedent=true -->
```csharp
var paris = new GeoPoint(new GeoPosition(2.35, 48.86));
var route = new GeoLineString([new(-1, 47), new(5, 47.5)]);
var park = new GeoPolygon([[new(2.2, 48.8), new(2.5, 48.8), new(2.5, 48.95), new(2.2, 48.95), new(2.2, 48.8)]]);

// The distance on the WGS 84 ellipsoid, in meters, to the nearest position of the line
Assert.Equal(174_431.755, paris.Distance(route)!.Value, 0.01);

// 0 for a point inside a polygon
Assert.Equal(0, paris.Distance(park));
```
<!-- endref -->

- The distance is 0 when the geometries meet: a position inside a polygon (and outside its holes), lines that cross or
  touch, a line inside a polygon. A position in a hole is at the distance to the edge of the hole.
- It is `null` when one of the geometries is empty. The altitudes are ignored.
- The distance is measured on the WGS 84 ellipsoid, as GPS devices do, with the formulas of Vincenty: it is accurate to
  a millimeter, except between nearly antipodal positions, where it is the great-circle distance (within 0.5%). Use
  [GeoPosition.GetDistance](cref:Meziantou.Framework.Geo.GeoPosition.GetDistance) for a faster distance on a sphere
  between two positions.
- The edges are straight lines in degrees, as for the other operations: the nearest position of an edge is a position
  of that straight line, and the inside of a polygon is the inside as tested by
  [GeoPolygon.Contains](cref:Meziantou.Framework.Geo.GeoPolygon.Contains).

## Buffers

[Buffer](cref:Meziantou.Framework.Geo.GeoGeometry.Buffer) returns the area within a distance of a geometry, in meters:
a circle around a point (a geofence), a corridor along a line, a polygon grown by a margin:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_Buffer;format=md-fenced-code-block;dedent=true -->
```csharp
// A geofence: the area within 500 meters of a position
var store = new GeoPoint(new GeoPosition(2.35, 48.86));
var geofence = Assert.IsType<GeoPolygon>(store.Buffer(500));
Assert.True(geofence.Contains(new GeoPosition(2.354, 48.86)));  // about 290 meters east
Assert.False(geofence.Contains(new GeoPosition(2.36, 48.86)));  // about 730 meters east

// A corridor of 1 kilometer on each side of a route, with flat ends
var route = new GeoLineString([new(2.35, 48.86), new(2.45, 48.90), new(2.55, 48.88)]);
var corridor = route.Buffer(1_000, new GeoBufferOptions { EndCapStyle = GeoBufferEndCapStyle.Flat });
```
<!-- endref -->

The result is a [GeoPolygon](cref:Meziantou.Framework.Geo.GeoPolygon), or a
[GeoMultiPolygon](cref:Meziantou.Framework.Geo.GeoMultiPolygon) when the parts do not touch: the circles around two
distant points stay apart. The buffers of the parts of a multi-geometry or a collection are united.

- A negative distance shrinks the polygons: their area without a band along their edges. A polygon narrower than twice
  the distance disappears. A point or a line has no buffer for a distance of 0 or less, and the method returns `null`.
- [GeoBufferOptions](cref:Meziantou.Framework.Geo.GeoBufferOptions) sets the shape of the buffer:
  - [SegmentsPerQuarterCircle](cref:Meziantou.Framework.Geo.GeoBufferOptions.SegmentsPerQuarterCircle), 8 by default:
    the circles are polygons of 32 sides. The edges of a circle are chords, a little inside the circle: 0.5% of the
    distance with 8 segments.
  - [JoinStyle](cref:Meziantou.Framework.Geo.GeoBufferOptions.JoinStyle), the shape on the outer side of a turn:
    [Round](cref:Meziantou.Framework.Geo.GeoBufferJoinStyle.Round) (an arc, the default),
    [Miter](cref:Meziantou.Framework.Geo.GeoBufferJoinStyle.Miter) (a sharp corner, cut beyond
    [MiterLimit](cref:Meziantou.Framework.Geo.GeoBufferOptions.MiterLimit) times the distance) or
    [Bevel](cref:Meziantou.Framework.Geo.GeoBufferJoinStyle.Bevel) (a straight cut).
  - [EndCapStyle](cref:Meziantou.Framework.Geo.GeoBufferOptions.EndCapStyle), the shape at the ends of a line:
    [Round](cref:Meziantou.Framework.Geo.GeoBufferEndCapStyle.Round) (the default),
    [Flat](cref:Meziantou.Framework.Geo.GeoBufferEndCapStyle.Flat) (the corridor stops at the end) or
    [Square](cref:Meziantou.Framework.Geo.GeoBufferEndCapStyle.Square) (it goes beyond the end by the distance).
- The positions at the distance are computed on the WGS 84 ellipsoid, and joined by edges straight in degrees. The
  pieces are combined as by [Union](cref:Meziantou.Framework.Geo.GeoGeometry.Union): the positions of the result are on
  the grid of ten-millionths of a degree.
- A buffer that contains a pole is not handled. A buffer that crosses the 180th meridian is not cut: its longitudes go
  beyond 180 or -180.

## Convex hull

[ConvexHull](cref:Meziantou.Framework.Geo.GeoGeometry.ConvexHull) returns the smallest convex polygon that contains
every position of a geometry, for instance the area covered by a set of stores or of GPS positions:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeometryTests.cs;region=GeoGeometry_ConvexHull;format=md-fenced-code-block;dedent=true -->
```csharp
// The positions of a few stores: the hull is the polygon of the outer ones
var stores = new GeoMultiPoint([new(2.35, 48.86), new(2.30, 48.84), new(2.40, 48.88), new(2.33, 48.90), new(2.36, 48.87)]);
var hull = Assert.IsType<GeoPolygon>(stores.ConvexHull());
Assert.HasCount(5, hull.Rings[0]); // four corners, and the first one again
```
<!-- endref -->

The hull is a polygon whose exterior ring turns counter-clockwise, made of positions of the geometry. It is a line
between the two ends when the positions are on a line, a point when they are all the same, and `null` for an empty
geometry. It is computed on the longitudes and latitudes, as on an equirectangular map.

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
disappear. The edges are straight lines in degrees, as on an equirectangular map, and the shapes that cross the 180th
meridian are not handled. The coordinates of the polygons must be between -100,000 and 100,000: convert projected
coordinates to degrees with [Transform](cref:Meziantou.Framework.Geo.GeoGeometry.Transform) first.
