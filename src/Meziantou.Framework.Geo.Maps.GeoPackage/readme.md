# Meziantou.Framework.Geo.Maps.GeoPackage

Reads and writes the map bundles of `Meziantou.Framework.Geo.Maps` as GeoPackage files: one file per region, such as
`France.gpkg`, with a table and an R-tree spatial index per layer, that QGIS, GDAL and ArcGIS open. The files are
GeoPackage 1.4, in WGS 84, and the metadata of the map is in the metadata extension.

## Usage

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

The `Meziantou.Framework.Geo.Maps.Tool` .NET tool writes GeoPackage bundles with `--format gpkg`. The package uses `Microsoft.Data.Sqlite`
and its native SQLite library.
