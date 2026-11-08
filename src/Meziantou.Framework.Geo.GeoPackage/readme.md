# Meziantou.Framework.Geo.GeoPackage

Reads the feature tables of GeoPackage files (`.gpkg`), the SQLite format that GDAL, QGIS and ArcGIS write, as the
features of `Meziantou.Framework.Geo`. A table is read whole, or one row at a time with bounded memory, and the R-tree
spatial index of a table reads only the features of a region.

## Usage

<!-- ref:../../tests/Meziantou.Framework.Geo.GeoPackage.Tests/GeoPackageTests.cs;region=GeoGeoPackage_ReadFeaturesAsync;format=md-fenced-code-block;dedent=true -->
```csharp
// The layers of a GeoPackage file are its feature tables
var tables = GeoPackage.GetTables("TestData/places.gpkg");
Assert.Contains("cities", tables);

// The rows are read one at a time; with a box, the R-tree spatial index of the table finds the rows to read
var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
var names = new List<string?>();
await foreach (var city in GeoPackage.ReadFeaturesAsync("TestData/places.gpkg", "cities", new GeoPackageReaderOptions { Bounds = europe }))
{
    names.Add((string?)city.Properties["name"]);
}

Assert.Contains("Paris", names);
Assert.DoesNotContain("Tokyo", names);
```
<!-- endref -->

The package uses `Microsoft.Data.Sqlite` and its native SQLite library.
