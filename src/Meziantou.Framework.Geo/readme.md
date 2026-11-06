# Meziantou.Framework.Geo

Reads and writes maps in GeoJSON, TopoJSON, Shapefile, KML, KMZ, GPX, WKT, WKB and encoded polylines, with features,
geometries and map projections. No dependency outside .NET.

## Usage

The types are in the `Meziantou.Framework.Geo` namespace. Every format is a static class with `Read`/`Parse` and
`Write`/`Format` methods, which return and take a `GeoFeatureCollection`.

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeoJsonTests.cs;region=Geo_GeoJsonReadWrite;format=md-fenced-code-block;dedent=true -->
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

Compute distances and test whether a position is inside an area:

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

Project positions onto a plane to draw a map:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/ProjectionTests.cs;region=Geo_Project;format=md-fenced-code-block;dedent=true -->
```csharp
var projection = new MercatorProjection();

// The point is in radii of the Earth, with y increasing to the north
var (x, y) = projection.Project(new GeoPosition(Longitude: 2.35, Latitude: 48.86));

// Scale and translate it to draw a map of 512 by 512 pixels, as the tiles of web maps
var pixelX = 256 + (x * 256 / Math.PI);
var pixelY = 256 - (y * 256 / Math.PI);
Assert.Equal(259.34, pixelX, 0.01);
Assert.Equal(176.14, pixelY, 0.01);

// Invert converts a point back to a position
var position = projection.Invert(x, y);
Assert.Equal(48.86, position!.Value.Latitude, 1e-9);
```
<!-- endref -->

## Formats

| Format | Class | Read | Write |
|---|---|---|---|
| GeoJSON | `GeoJson` | ✓ | ✓ |
| TopoJSON | `TopoJson` | ✓ | ✓ |
| Shapefile (`.shp`, `.dbf`, zipped) | `Shapefile` | ✓ | ✓ |
| KML / KMZ | `Kml`, `Kmz` | ✓ | ✓ |
| GPX | `Gpx` | ✓ | ✓ |
| WKT / WKB (and PostGIS EWKT / EWKB) | `WellKnownText`, `WellKnownBinary` | ✓ | ✓ |
| Encoded polyline | `EncodedPolyline` | ✓ | ✓ |

The positions are longitudes and latitudes in degrees (WGS 84): the library does not convert between coordinate
reference systems.
