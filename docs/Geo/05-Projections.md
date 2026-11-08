---
title: Projections
description: Project longitudes and latitudes onto a plane to draw a map, and convert data in UTM, Lambert 93, Web Mercator or another coordinate reference system to longitudes and latitudes.
---

A projection flattens the globe: [Project](cref:Meziantou.Framework.Geo.GeoProjection.Project) converts a position to a
point of a plane, in radii of the Earth, with y increasing to the north. Scale and translate the point to draw it:

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

[Invert](cref:Meziantou.Framework.Geo.GeoProjection.Invert) converts a point back to a position, for instance to find
the position under the mouse. It returns `null` for a point outside of the map.

## Choose a projection

| Projection | Use it for |
|---|---|
| [EquirectangularProjection](cref:Meziantou.Framework.Geo.EquirectangularProjection) | the simplest map: the longitude is x and the latitude is y |
| [MercatorProjection](cref:Meziantou.Framework.Geo.MercatorProjection) | the maps of web sites: it keeps the shapes, and enlarges the areas far from the equator |
| [EqualEarthProjection](cref:Meziantou.Framework.Geo.EqualEarthProjection) | world maps that compare areas: the areas are in proportion to the areas of the regions |
| [AlbersProjection](cref:Meziantou.Framework.Geo.AlbersProjection) | the maps of a country or a continent: equal-area, centered on its [CentralMeridian](cref:Meziantou.Framework.Geo.AlbersProjection.CentralMeridian) |
| [AzimuthalEquidistantProjection](cref:Meziantou.Framework.Geo.AzimuthalEquidistantProjection) | the maps of a polar region, such as Antarctica or the Arctic: centered on the North Pole, or on the South Pole with [SouthPole](cref:Meziantou.Framework.Geo.AzimuthalEquidistantProjection.SouthPole) |

- The Mercator projection cannot show the poles: it limits the latitudes to
  [MaxLatitude](cref:Meziantou.Framework.Geo.GeoProjection.MaxLatitude), about 85.05°, where the world map is a square,
  as on the tiles of web maps.
- The Albers projection measures the longitudes from its central meridian and does not cut the shapes that cross the
  opposite meridian: give it the regions of the map, not the whole world. Its defaults are those of the maps of the
  contiguous United States.
- The azimuthal equidistant projection keeps the distances from the pole: the meridians are lines from the pole and
  the parallels circles around it. The whole world is a disc whose edge is the opposite pole, so the regions far from
  the pole are stretched: use it for the regions around a pole. On the map of the South Pole, its
  [CentralMeridian](cref:Meziantou.Framework.Geo.AzimuthalEquidistantProjection.CentralMeridian) points up; on the
  map of the North Pole, it points down.
- [HasStraightMeridians](cref:Meziantou.Framework.Geo.GeoProjection.HasStraightMeridians) and
  [HasStraightParallels](cref:Meziantou.Framework.Geo.GeoProjection.HasStraightParallels) tell whether a line along a
  meridian or a parallel stays straight on the map. When it does not, add intermediate positions to draw it as a curve.

The projections only convert positions: they do not cut the lines that cross the edge of the map. To draw maps as SVG
with these projections, use the [maps of Meziantou.Framework.Charts](../Charts/04-Gallery/09-Maps.md).

## Write your own projection

Derive from [GeoProjection](cref:Meziantou.Framework.Geo.GeoProjection) and implement `ProjectRadians`, which receives
the longitude and the latitude in radians. Override `InvertRadians` to support
[Invert](cref:Meziantou.Framework.Geo.GeoProjection.Invert), and the properties above when the defaults do not apply.

## Convert between coordinate reference systems

Many datasets are not in longitudes and latitudes: the open data of France is in Lambert 93, many national datasets in
a UTM zone, and the tiles of web maps in Web Mercator, with coordinates in meters. A
[CoordinateReferenceSystem](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem) says what the coordinates mean,
and [Transform](cref:Meziantou.Framework.Geo.GeoGeometry.Transform) converts every position of a geometry from one
system to another:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/ProjectionTests.cs;region=Geo_CoordinateReferenceSystemTransform;format=md-fenced-code-block;dedent=true -->
```csharp
// A point of a dataset in Lambert 93 (EPSG:2154), in meters
var lambert93 = CoordinateReferenceSystem.Lambert93;
var eiffelTower = new GeoPoint(new GeoPosition(648237.30, 6862271.68));

// Transform converts every position of a geometry
var wgs84 = (GeoPoint)eiffelTower.Transform(lambert93, CoordinateReferenceSystem.Wgs84);
Assert.Equal(2.2945, wgs84.Position.Longitude, 1e-7);
Assert.Equal(48.8584, wgs84.Position.Latitude, 1e-7);

// Or convert a position to any other coordinate reference system, such as a UTM zone
var utm = CoordinateReferenceSystem.Utm(31, isNorthernHemisphere: true);
var position = lambert93.Transform(new GeoPosition(648237.30, 6862271.68), utm);
Assert.Equal(448252.0, position.Longitude, 0.1);
Assert.Equal(5411954.9, position.Latitude, 0.1);
```
<!-- endref -->

A [GeoPosition](cref:Meziantou.Framework.Geo.GeoPosition) holds the coordinates of any system in the GIS order:
[Longitude](cref:Meziantou.Framework.Geo.GeoPosition.Longitude) is the easting (x) and
[Latitude](cref:Meziantou.Framework.Geo.GeoPosition.Latitude) the northing (y), whatever the order of the axes in the
definition of the system. The positions go from one system to another through WGS 84, with
[ToWgs84](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.ToWgs84) and
[FromWgs84](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.FromWgs84). The projections keep the altitude; the
datum shifts change it, as an ellipsoidal height.

The [geometry operations](06-GeometryOperations.md) work on longitudes and latitudes: convert projected data to
[Wgs84](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.Wgs84) first.

## Get a coordinate reference system

- [Wgs84](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.Wgs84),
  [WebMercator](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.WebMercator) and
  [Lambert93](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.Lambert93) are the most common ones, and
  [Utm](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.Utm) gives a UTM zone of WGS 84.
- [FromEpsg](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.FromEpsg) gets a system by its EPSG code, such as
  the SRID of PostGIS. It returns `null` for a code it does not know.
- [ParseWkt](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem.ParseWkt) reads the definition of a `.prj` file, in
  the WKT of the OGC or of ESRI. A definition with an EPSG code that `FromEpsg` knows gives that system; otherwise it is
  built from the ellipsoid, the datum shift (`TOWGS84`), the prime meridian, the units and the parameters of its
  projection. It throws a `NotSupportedException` for a projection that the library does not have.

| EPSG codes | Coordinate reference system |
|---|---|
| 4326, 4258, 4171, 4269 | WGS 84, ETRS89, RGF93 v1, NAD83 (longitudes and latitudes) |
| 4275, 4277 | NTF, OSGB36 (longitudes and latitudes) |
| 3857 (and 3785, 900913, 102100) | Web Mercator |
| 3395 | World Mercator |
| 32601 to 32660, 32701 to 32760 | the UTM zones of WGS 84, north and south |
| 25828 to 25838, 26901 to 26923 | the UTM zones of ETRS89 and of NAD83 |
| 2154, 3942 to 3950 | Lambert 93 and the conic conformal zones CC42 to CC50 of France |
| 27572 | NTF (Paris) / Lambert zone II, the "Lambert II étendu" |
| 27700 | OSGB36 / British National Grid |

The projections are the transverse Mercator (UTM, Gauss–Krüger), the Lambert conformal conic with one or two standard
parallels, and the Mercator on an ellipsoid or on the sphere of Web Mercator. They give the values of PROJ to a
millimeter.

The library does not use grids of datum shifts. ETRS89, RGF93 and NAD83 are taken as equal to WGS 84: they differ by
less than two meters, which is the accuracy of the conversion between them and WGS 84 without a grid. NTF, OSGB36 and
the datums whose definition has a `TOWGS84` element are shifted with a Helmert transformation, accurate to a few
meters. The other datums are taken as equal to WGS 84: give their
[ToWgs84Transformation](cref:Meziantou.Framework.Geo.GeographicCoordinateSystem.ToWgs84Transformation) to shift them.

## Read the coordinate reference system of a file

The extended WKT and WKB of PostGIS give an SRID, usually an EPSG code:
[WellKnownText.Parse](cref:Meziantou.Framework.Geo.WellKnownText.Parse) and
[WellKnownBinary.Read](cref:Meziantou.Framework.Geo.WellKnownBinary.Read) return it. The coordinates are those of the
text:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/WellKnownTextTests.cs;region=Geo_WellKnownTextSrid;format=md-fenced-code-block;dedent=true -->
```csharp
// The extended WKT of PostGIS gives the SRID of the coordinates, here Lambert 93 (EPSG:2154)
var geometry = WellKnownText.Parse("SRID=2154;POINT (652310.72 6862414.64)", out var srid);
Assert.Equal(2154, srid);

// FromEpsg gets the coordinate reference system of the SRID, to convert the geometry to longitudes and latitudes
var crs = CoordinateReferenceSystem.FromEpsg(srid!.Value)!;
var point = (GeoPoint)geometry.Transform(crs, CoordinateReferenceSystem.Wgs84);
Assert.Equal(2.35, point.Position.Longitude, 1e-6);
Assert.Equal(48.86, point.Position.Latitude, 1e-6);
```
<!-- endref -->

The reader of shapefiles reads the `.prj` file into the
[CoordinateReferenceSystem](cref:Meziantou.Framework.Geo.GeoFeatureCollection.CoordinateReferenceSystem) of the
features, and converts the positions to longitudes and latitudes when
[TransformToWgs84](cref:Meziantou.Framework.Geo.ShapefileReaderOptions.TransformToWgs84) is set:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/ShapefileTests.cs;region=Geo_ShapefileReadProjected;format=md-fenced-code-block;dedent=true -->
```csharp
// The .prj file declares Lambert 93: the positions are converted to longitudes and latitudes
var features = Shapefile.Read(path, new ShapefileReaderOptions { TransformToWgs84 = true });
var position = ((GeoPoint)features[0].Geometry!).Position;
Assert.Equal(2.35, position.Longitude, 1e-6);
Assert.Equal(48.86, position.Latitude, 1e-6);

// Without the option, the positions are those of the file, and CoordinateReferenceSystem tells what they are
var projected = Shapefile.Read(path);
Assert.Equal("RGF_1993_Lambert_93", projected.CoordinateReferenceSystem!.Name);
Assert.False(projected.CoordinateReferenceSystem.IsGeographic);
```
<!-- endref -->

## Add a coordinate reference system

A system whose projection is supported can be built from its parameters: a
[GeographicCoordinateSystem](cref:Meziantou.Framework.Geo.GeographicCoordinateSystem) (an
[Ellipsoid](cref:Meziantou.Framework.Geo.Ellipsoid), a datum shift, a prime meridian) projected by a
[TransverseMercatorConversion](cref:Meziantou.Framework.Geo.TransverseMercatorConversion), a
[LambertConformalConicConversion](cref:Meziantou.Framework.Geo.LambertConformalConicConversion) or a
[MercatorConversion](cref:Meziantou.Framework.Geo.MercatorConversion), in a
[ProjectedCoordinateSystem](cref:Meziantou.Framework.Geo.ProjectedCoordinateSystem). Derive from
[CoordinateConversion](cref:Meziantou.Framework.Geo.CoordinateConversion) for another projection method.

For any other system, derive from [CoordinateReferenceSystem](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem)
and implement `ToWgs84` and `FromWgs84`, for instance with the formulas of a national agency, or by calling another
library. Map the SRIDs and the `.prj` files you read to it:

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/ProjectionTests.cs;region=Geo_CustomCoordinateReferenceSystem;format=md-fenced-code-block;dedent=true -->
```csharp
// The Swiss coordinates LV95 (EPSG:2056), with the approximate formulas of swisstopo (about a meter)
private sealed class SwissLv95CoordinateSystem() : CoordinateReferenceSystem("CH1903+ / LV95")
{
    public override bool IsGeographic => false;

    public override GeoPosition ToWgs84(GeoPosition position)
    {
        var y = (position.Longitude - 2_600_000) / 1_000_000;
        var x = (position.Latitude - 1_200_000) / 1_000_000;
        var lambda = 2.6779094 + (4.728982 * y) + (0.791484 * y * x) + (0.1306 * y * x * x) - (0.0436 * y * y * y);
        var phi = 16.9023892 + (3.238272 * x) - (0.270978 * y * y) - (0.002528 * x * x) - (0.0447 * y * y * x) - (0.0140 * x * x * x);
        return new GeoPosition(lambda * 100 / 36, phi * 100 / 36, position.Altitude);
    }

    public override GeoPosition FromWgs84(GeoPosition position)
    {
        var phi = ((position.Latitude * 3600) - 169_028.66) / 10_000;
        var lambda = ((position.Longitude * 3600) - 26_782.5) / 10_000;
        var easting = 2_600_072.37 + (211_455.93 * lambda) - (10_938.51 * lambda * phi) - (0.36 * lambda * phi * phi) - (44.54 * lambda * lambda * lambda);
        var northing = 1_200_147.07 + (308_807.95 * phi) + (3_745.25 * lambda * lambda) + (76.63 * phi * phi) - (194.56 * lambda * lambda * phi) + (119.79 * phi * phi * phi);
        return new GeoPosition(easting, northing, position.Altitude);
    }
}
```
<!-- endref -->
