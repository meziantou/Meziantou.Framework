---
title: Limitations
description: What the library does not do: coordinate systems, curved edges, per-point data and other formats.
---

## Coordinates

- **WGS 84 only.** The positions are longitudes and latitudes in degrees. The library does not convert between
  coordinate reference systems: the SRID of EWKT and EWKB and the `.prj` file of a shapefile are not used.
  [Transform](cref:Meziantou.Framework.Geo.GeoGeometry.Transform) applies a conversion you provide, such as the inverse
  of the Mercator projection for Web Mercator data; convert other projected data, such as a shapefile in Lambert 93 or
  UTM, with another tool first.
- **Straight edges in degrees.** [GeoPolygon.Contains](cref:Meziantou.Framework.Geo.GeoPolygon.Contains),
  [GetBounds](cref:Meziantou.Framework.Geo.GeoGeometry.GetBounds),
  [LabelPosition](cref:Meziantou.Framework.Geo.GeoGeometry.LabelPosition) and the
  [geometry operations](06-GeometryOperations.md) treat the edges as straight lines on an equirectangular map, not as
  great circles, and do not handle the shapes that cross the 180th meridian.

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
