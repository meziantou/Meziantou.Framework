---
title: Limitations
description: What the library does not do: some projections and datum grids, curved edges, buffers, per-point data and other formats.
---

## Coordinates

- **Few projections, no grids.** The [coordinate reference systems](05-Projections.md#convert-between-coordinate-reference-systems)
  are those of the transverse Mercator, Lambert conformal conic and Mercator projections, such as UTM, Lambert 93 and
  Web Mercator. Other projections, such as the oblique Mercator of Switzerland, the Albers or the Lambert azimuthal
  equal-area projections of statistical maps, and the state plane systems that use them, need a
  [CoordinateReferenceSystem](cref:Meziantou.Framework.Geo.CoordinateReferenceSystem) of your own. The datums are
  shifted with Helmert transformations, not with grids, so a conversion between datums is accurate to a few meters, and
  the WKT 2 definitions are only read by their EPSG code. The writers do not write the SRID of EWKT and EWKB, and
  write a `.prj` file of WGS 84.
- **Straight edges in degrees.** [GeoPolygon.Contains](cref:Meziantou.Framework.Geo.GeoPolygon.Contains),
  [GetBounds](cref:Meziantou.Framework.Geo.GeoGeometry.GetBounds),
  [LabelPosition](cref:Meziantou.Framework.Geo.GeoGeometry.LabelPosition) and the
  [geometry operations](06-GeometryOperations.md) treat the edges as straight lines on an equirectangular map, not as
  great circles, and do not handle the shapes that cross the 180th meridian.
- **No buffers.** The [geometry operations](06-GeometryOperations.md) repair, clip, simplify and combine polygons,
  but do not compute buffers, distances between geometries or convex hulls.

## Data that the model does not keep

- **One position, no time.** A [GeoPosition](cref:Meziantou.Framework.Geo.GeoPosition) has a longitude, a latitude
  and an altitude. The data of each point of a GPX track or of a `gx:Track`, such as its time or the heart rate, and
  the M values (measures) of WKT, WKB and shapefiles, are dropped.
- **No styles.** The styles of KML and the icons of KMZ files are not read.
- **No folders.** The folders of a KML document become a `folder` property, and are not written back.

## Formats

The library does not read GeoParquet, GeoPackage, MapInfo, GML or CSV files. A CSV file whose column holds
WKT can be read with `Meziantou.Framework.Csv` and [WellKnownText](cref:Meziantou.Framework.Geo.WellKnownText).

The readers load the whole document in memory: they do not stream the features of large files. The FlatGeobuf reader
only decodes the features of a region when the file has a spatial index, but reads the whole file.
