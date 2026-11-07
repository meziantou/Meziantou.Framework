---
title: Formats
description: Store the bundles as folders of FlatGeobuf or GeoJSON files, or as GeoPackage files, read them with a catalog, and convert a bundle from one format to another.
---

A bundle holds the same layers and metadata in every format, and [Map](cref:Meziantou.Framework.Geo.Maps.Map) reads
them the same way. The format decides how the bundle is stored, and which tools can open it:

| Format | Storage | Choose it for |
|---|---|---|
| [MapFormat.FlatGeobuf](cref:Meziantou.Framework.Geo.Maps.MapFormat.FlatGeobuf) | a folder per region, a `.fgb` file per layer and a `metadata.json` file | the default: compact, with a spatial index, no dependency |
| [MapFormat.GeoJson](cref:Meziantou.Framework.Geo.Maps.MapFormat.GeoJson) | a folder per region, a `.geojson` file per layer and a `metadata.json` file | text that web libraries (Leaflet, D3) load as they are, and that diffs well; larger, and without spatial index |
| [GeoPackageMapFormat](cref:Meziantou.Framework.Geo.Maps.GeoPackage.GeoPackageMapFormat) | one `.gpkg` file per region, with a table and a spatial index per layer | one file per region that QGIS, GDAL and ArcGIS open with all its layers; needs the native SQLite library |

The [Meziantou.Framework.Geo.Maps.Tool .NET tool](03-GeneratingBundles.md) writes the format of its `--format` option. The layer files are named after
[GetLayerName](cref:Meziantou.Framework.Geo.Maps.MapFormat.GetLayerName), such as `borders` or `states-provinces`.

## Read the folder formats

A [MapCatalog](cref:Meziantou.Framework.Geo.Maps.MapCatalog) reads the two folder formats by default: a folder of
FlatGeobuf files and a folder of GeoJSON files are both maps. A folder holds one map: writing a map in one folder format
removes the layers of the other.

The properties that [MapLayer](cref:Meziantou.Framework.Geo.Maps.MapLayer) documents have the same type in every
format. GeoJSON has no integer type, so its numbers are read as `double` values; the map converts the documented ones,
such as the `rank` of a city to an `int`. The other properties keep the type the format reads.

## Read GeoPackage files

The GeoPackage format is in its own package, so that the applications that read folders do not depend on SQLite:

<InstallNuGetPackage Id="Meziantou.Framework.Geo.Maps.GeoPackage" />

Create the catalog with [GeoPackageMapFormat.Instance](cref:Meziantou.Framework.Geo.Maps.GeoPackage.GeoPackageMapFormat.Instance)
in its formats. They are in order of preference: when a region has a map in two formats, the first one wins.

<!-- ref:../../tests/Meziantou.Framework.Geo.Maps.GeoPackage.Tests/GeoPackageMapFormatTests.cs;region=GeoMapsGeoPackage_Catalog;format=md-fenced-code-block;dedent=true -->
```csharp
// Write the map of a region as a GeoPackage file, which QGIS and GDAL open, such as maps/Switzerland.gpkg
var directory = Directory.CreateTempSubdirectory().FullName;
var switzerland = MapCatalog.Default.GetMap("Switzerland");
GeoPackageMapFormat.Instance.Write(GeoPackageMapFormat.Instance.GetPath(directory, switzerland.Region), switzerland);

// A catalog reads the GeoPackage files when it has the format, here before the folder formats
var catalog = new MapCatalog(directory, [GeoPackageMapFormat.Instance, .. MapCatalog.DefaultFormats]);
Assert.Equal(["Switzerland"], catalog.Regions);

var map = catalog.GetMap("Switzerland");
Assert.HasCount(26, map.Load(MapLayer.StatesProvinces));
```
<!-- endref -->

[MapCatalog.Default](cref:Meziantou.Framework.Geo.Maps.MapCatalog.Default) reads only the folder formats: create a
catalog of its [Directory](cref:Meziantou.Framework.Geo.Maps.MapCatalog.Directory) with the GeoPackage format to read
the GeoPackage bundles that packages copy to the `maps` folder (`pack --package-content files`). A catalog that finds a map in a format it does not read
names it in the message of the `KeyNotFoundException` of [GetMap](cref:Meziantou.Framework.Geo.Maps.MapCatalog.GetMap).

A layer is the table named after the layer with underscores, such as `states_provinces`, and the metadata of the map,
the JSON of `metadata.json`, is in the metadata extension of the file. Its columns have the GeoPackage types of the
properties: `rank` is a `MEDIUMINT`, `population` an `INTEGER` and `capital` a `BOOLEAN`. SQLite does not store NaN: a
`NaN` value is written as `NULL`, so the feature does not have the property once read.

## Convert a bundle

[Write](cref:Meziantou.Framework.Geo.Maps.MapFormat.Write) with a map writes all its layers and its metadata in a
format, such as a GeoJSON copy of a bundle for a web page:

<!-- ref:../../tests/Meziantou.Framework.Geo.Maps.Tests/MapCatalogTests.cs;region=GeoMaps_ConvertFormat;format=md-fenced-code-block;dedent=true -->
```csharp
// Write the map of a region in another format: a folder of GeoJSON files, which web libraries read
var output = Directory.CreateTempSubdirectory().FullName;
var map = MapCatalog.Default.GetMap("Switzerland");
MapFormat.GeoJson.Write(MapFormat.GeoJson.GetPath(output, map.Region), map);
Assert.True(File.Exists(Path.Combine(output, "Switzerland", "states-provinces.geojson")));

// The default catalog reads both folder formats
var copy = new MapCatalog(output).GetMap("Switzerland");
Assert.HasCount(26, copy.Load(MapLayer.StatesProvinces));
```
<!-- endref -->

[GetPath](cref:Meziantou.Framework.Geo.Maps.MapFormat.GetPath) gives the path of the map of a region in the folder of a
catalog: a folder for the folder formats, a file for GeoPackage. Writing a map replaces the map at this path.

## Other formats

Derive from [MapFormat](cref:Meziantou.Framework.Geo.Maps.MapFormat) to store the maps another way: implement
[TryGetRegion](cref:Meziantou.Framework.Geo.Maps.MapFormat.TryGetRegion) to recognize a map in the folder of a
catalog, [Open](cref:Meziantou.Framework.Geo.Maps.MapFormat.Open) to return a [Map](cref:Meziantou.Framework.Geo.Maps.Map)
that loads its layers, and [Write](cref:Meziantou.Framework.Geo.Maps.MapFormat.Write). [FormatMetadata](cref:Meziantou.Framework.Geo.Maps.MapFormat.FormatMetadata)
and [ParseMetadata](cref:Meziantou.Framework.Geo.Maps.MapFormat.ParseMetadata) read and write the metadata as the JSON of `metadata.json`.
