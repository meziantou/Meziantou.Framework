---
title: Limitations
description: What the library does not do: some projections and datum grids, curved edges, per-point data and other formats.
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
- **Straight edges for the geometry operations.** The [geometry operations](06-GeometryOperations.md) and
  [LabelPosition](cref:Meziantou.Framework.Geo.GeoGeometry.LabelPosition) treat the edges as straight lines on an
  equirectangular map. Only [GeoPolygon.Contains](cref:Meziantou.Framework.Geo.GeoPolygon.Contains) and
  [GetBounds](cref:Meziantou.Framework.Geo.GeoGeometry.GetBounds) offer
  [great-circle edges](03-FeaturesAndGeometries.md#great-circles), computed on a sphere, not on the ellipsoid. Split
  the shapes that cross the 180th meridian with
  [SplitAtAntimeridian](cref:Meziantou.Framework.Geo.GeoGeometry.SplitAtAntimeridian) before the operations.

## Data that the model does not keep

- **No sensor data.** A [GeoPosition](cref:Meziantou.Framework.Geo.GeoPosition) keeps a time and a measure, but not
  the other data of each point of a GPX track or of a `gx:Track`, such as the heart rate or the cadence of their
  extensions.
- **No styles.** The styles of KML and the icons of KMZ files are not read.
- **No folders.** The folders of a KML document become a `folder` property, and are not written back.

## Formats

The library does not read MapInfo, GML or CSV files, and does not write GeoPackage and GeoParquet files. GeoParquet
geometries in the native (GeoArrow) encodings are not read: only WKB is. A CSV file whose column holds WKT can be read
with `Meziantou.Framework.Csv` and [WellKnownText](cref:Meziantou.Framework.Geo.WellKnownText).

The readers of TopoJSON, KML, KMZ, GPX, WKT and WKB load the whole document in memory. GeoJSON, shapefiles,
FlatGeobuf, GeoPackage and GeoParquet files can be read with bounded memory with their `ReadFeaturesAsync` method, as
explained in [formats](04-Formats/01-Overview.md).
