---
title: WKT and WKB
description: Read and write single geometries as well-known text and well-known binary, as databases such as PostGIS store them.
---

Well-known text (WKT) and well-known binary (WKB) are the formats of a single geometry in databases (PostGIS, SQL
Server, SQLite), GeoParquet files and CSV files. They have no properties: they read and write a
[GeoGeometry](cref:Meziantou.Framework.Geo.GeoGeometry).

## WKT

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/WellKnownTextTests.cs;region=Geo_WellKnownTextParse;format=md-fenced-code-block;dedent=true -->
```csharp
// A column of a CSV export, or the result of ST_AsText in a database
var geometry = WellKnownText.Parse("POLYGON ((2.25 48.82, 2.42 48.82, 2.42 48.90, 2.25 48.90, 2.25 48.82))");

var polygon = Assert.IsType<GeoPolygon>(geometry);
Assert.True(polygon.Contains(new GeoPosition(2.35, 48.86)));
Assert.Equal(new GeoBounds(2.25, 48.82, 2.42, 48.90), geometry.GetBounds());
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/WellKnownTextTests.cs;region=Geo_WellKnownTextFormat;format=md-fenced-code-block;dedent=true -->
```csharp
var route = new GeoLineString([new GeoPosition(2.35, 48.86, 35), new GeoPosition(4.84, 45.76, 170)]);

var wkt = WellKnownText.Format(route);

Assert.Equal("LINESTRING Z (2.35 48.86 35, 4.84 45.76 170)", wkt);
```
<!-- endref -->

## WKB

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/WellKnownBinaryTests.cs;region=Geo_WellKnownBinaryReadWrite;format=md-fenced-code-block;dedent=true -->
```csharp
// The WKB of a geometry column, as read from a database or a GeoParquet file
var polygon = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 0)]]);
byte[] wkb = WellKnownBinary.Write(polygon);

var geometry = WellKnownBinary.Read(wkb);

Assert.Equal(GeoGeometryType.Polygon, geometry.Type);
Assert.Equal(polygon.Rings[0], ((GeoPolygon)geometry).Rings[0]);
```
<!-- endref -->

PostGIS returns geometries as hexadecimal text. [ParseHex](cref:Meziantou.Framework.Geo.WellKnownBinary.ParseHex) and
[FormatHex](cref:Meziantou.Framework.Geo.WellKnownBinary.FormatHex) read and write it:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/WellKnownBinaryTests.cs;region=Geo_WellKnownBinaryHex;format=md-fenced-code-block;dedent=true -->
```csharp
// PostGIS writes a geometry value as the hexadecimal text of its extended WKB (here with SRID 4326)
var geometry = WellKnownBinary.ParseHex("0101000020E6100000A835CD3B4ED1024076E09C11A56D4840");
Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)geometry).Position);

// PostGIS reads this text back with ST_GeomFromWKB(decode(@hex, 'hex'), 4326)
var hex = WellKnownBinary.FormatHex(geometry);
Assert.Equal("0101000000A835CD3B4ED1024076E09C11A56D4840", hex);
```
<!-- endref -->

## What is kept

- The seven geometry types, with the Z values as altitudes. The M values (measures) are dropped, and the curves,
  triangles and surfaces are rejected.
- The extended formats of PostGIS: the `SRID=4326;` prefix of EWKT and the flags of EWKB. The SRID is ignored: the
  positions must be longitudes and latitudes.
- WKB in both byte orders. The writer writes ISO WKB in little-endian order.
- An empty point (`POINT EMPTY`) is read as an empty [GeoMultiPoint](cref:Meziantou.Framework.Geo.GeoMultiPoint), as a
  [GeoPoint](cref:Meziantou.Framework.Geo.GeoPoint) always has a position.
- A geometry has one number of dimensions. When a position has an altitude, the writers write an altitude for every
  position, and 0 for those that have none.
