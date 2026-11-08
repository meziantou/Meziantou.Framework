---
title: Overview
description: The supported formats, what each one is for, and how to convert a file from one format to another.
---

Every format is a static class. The classes of the formats that hold features have the same methods: `Read` reads a
stream, `Parse` reads a string, `Write` writes to a stream and `Format` returns a string. They all return and take a
[GeoFeatureCollection](cref:Meziantou.Framework.Geo.GeoFeatureCollection). GeoJSON, Shapefile, FlatGeobuf, GeoPackage and
GeoParquet also have `ReadFeaturesAsync`, which returns the features of a large file one at a time instead of a collection.

| Format | Class | Use it for |
|---|---|---|
| [GeoJSON](02-GeoJson.md) | [GeoJson](cref:Meziantou.Framework.Geo.GeoJson) | web maps and APIs: the default choice |
| [TopoJSON](03-TopoJson.md) | [TopoJson](cref:Meziantou.Framework.Geo.TopoJson) | compact maps of regions that share borders, such as `world-atlas` |
| [Shapefile](04-Shapefile.md) | [Shapefile](cref:Meziantou.Framework.Geo.Shapefile) | the data of GIS software and of public agencies |
| [KML and KMZ](05-KmlAndKmz.md) | [Kml](cref:Meziantou.Framework.Geo.Kml), [Kmz](cref:Meziantou.Framework.Geo.Kmz) | Google Earth and Google My Maps |
| [GPX](06-Gpx.md) | [Gpx](cref:Meziantou.Framework.Geo.Gpx) | GPS devices and sport apps: waypoints, routes and tracks |
| [WKT and WKB](07-WktAndWkb.md) | [WellKnownText](cref:Meziantou.Framework.Geo.WellKnownText), [WellKnownBinary](cref:Meziantou.Framework.Geo.WellKnownBinary) | a single geometry, in a database (PostGIS, SQL Server, SQLite) or a CSV file |
| [Encoded polyline](08-EncodedPolyline.md) | [EncodedPolyline](cref:Meziantou.Framework.Geo.EncodedPolyline) | the routes of Google Maps, OSRM and Valhalla |
| [FlatGeobuf](09-FlatGeobuf.md) | [FlatGeobuf](cref:Meziantou.Framework.Geo.FlatGeobuf) | large datasets: compact, fast to read, with a spatial index to read a region |
| [GeoPackage](10-GeoPackage.md) | [GeoPackage](cref:Meziantou.Framework.Geo.GeoPackage) | the layers of GDAL, QGIS and ArcGIS in a SQLite file; read only, in the `Meziantou.Framework.Geo.GeoPackage` package |
| [GeoParquet](11-GeoParquet.md) | [GeoParquet](cref:Meziantou.Framework.Geo.GeoParquet) | large datasets in Parquet files, as data lakes and cloud catalogs store them; read only, in the `Meziantou.Framework.Geo.GeoParquet` package |

## Convert a file

Read the file with the class of its format and write the features with the class of the other one. Each page says what
a format keeps: a format that has no place for something drops it. For instance, GPX has no polygons, and Shapefile
holds a single type of geometry.

## Errors and security

- Invalid data throws a `FormatException`, with the error of the underlying parser, such as a `JsonException` or an
  `XmlException`, as its inner exception.
- A position that is not a finite number cannot be written: the writers throw an `ArgumentException`.
- The XML readers process no document type definition and resolve no external entity, and no reader downloads
  anything. The binary readers check every count against the size of the data before they allocate.
- `Read` and `Parse` load the whole document in memory: check the size of the files you accept from users. The
  `ReadFeaturesAsync` methods of [GeoJson](cref:Meziantou.Framework.Geo.GeoJson),
  [Shapefile](cref:Meziantou.Framework.Geo.Shapefile), [FlatGeobuf](cref:Meziantou.Framework.Geo.FlatGeobuf) and
  [GeoPackage](cref:Meziantou.Framework.Geo.GeoPackage) read
  the features one at a time, with bounded memory: only the feature being read is in memory, so its size is the limit.
  The one of [GeoParquet](cref:Meziantou.Framework.Geo.GeoParquet) holds a row group at a time.
