---
title: Projections
description: Project longitudes and latitudes onto a plane to draw a map, and convert points back to positions.
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
