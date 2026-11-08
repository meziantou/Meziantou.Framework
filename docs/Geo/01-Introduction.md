---
title: Introduction
description: Read, convert and write maps in the common geographic formats, and work with features, geometries and projections.
---

`Meziantou.Framework.Geo` reads and writes maps: countries, roads, GPS tracks, points of interest. It has no
dependency outside .NET, and it supports trimming.

## What you can do with it

- **Read and write the common formats**: GeoJSON, TopoJSON, Shapefile, KML and KMZ, GPX, WKT and WKB, encoded
  polylines, and FlatGeobuf, and read GeoPackage files with the `Meziantou.Framework.Geo.GeoPackage` package. Every
  reader returns the same model, so converting a file is a read and a write, and GeoJSON, Shapefile, FlatGeobuf and
  GeoPackage files can be read one feature at a time. See [formats](04-Formats/01-Overview.md).
- **Work with features and geometries**: points, lines, polygons and their collections, with their properties, and
  positions with an optional altitude, measure and time, such as the points of a GPS track; get their bounds, a
  position to label them, distances, and whether a position is inside an area, with straight or great-circle edges and
  across the 180th meridian. See
  [features and geometries](03-FeaturesAndGeometries.md).
- **Project positions** onto a plane to draw a map, with the equirectangular, Mercator, Equal Earth, Albers and
  azimuthal equidistant projections. See [projections](05-Projections.md).
- **Transform geometries**: repair invalid polygons, clip to a box, simplify, split along the 180th meridian, combine
  polygons (union, intersection, difference), measure the distance between geometries, compute buffers and convex
  hulls, and convert positions from another coordinate system. See
  [geometry operations](06-GeometryOperations.md).
- **Draw maps**: `Meziantou.Framework.Charts` draws the features as choropleths and points on maps. See
  [maps](../Charts/04-Gallery/09-Maps.md).

The positions are longitudes and latitudes in degrees, on WGS 84, as GPS devices, GeoJSON and web maps use them.
[Coordinate reference systems](05-Projections.md#convert-between-coordinate-reference-systems) convert data in UTM,
Lambert 93, Web Mercator and other projections to them.

## Namespace

Every type is in the [Meziantou.Framework.Geo](cref:Meziantou.Framework.Geo) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read a file and write one.
- [Limitations](07-Limitations.md): what the library does not do.
