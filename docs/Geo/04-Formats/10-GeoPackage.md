---
title: GeoPackage
description: Read the feature tables of GeoPackage files, the SQLite format of GDAL, QGIS and ArcGIS, whole or one row at a time, and only the features of a region.
---

[GeoPackage](https://www.geopackage.org/) is the SQLite database that GDAL, QGIS and ArcGIS use to store layers: each
layer is a table of features, often with an R-tree spatial index. The reader is in the `Meziantou.Framework.Geo.GeoPackage`
package, so that the main package does not depend on SQLite:

```shell
dotnet add package Meziantou.Framework.Geo.GeoPackage
```

## Read a table

[GeoPackage.GetTables](cref:Meziantou.Framework.Geo.GeoPackage.GetTables) lists the feature tables of a file, and
[GeoPackage.Read](cref:Meziantou.Framework.Geo.GeoPackage.Read) reads one. The table name can be omitted when the file
has a single one:

<!-- ref:../../../tests/Meziantou.Framework.Geo.GeoPackage.Tests/GeoPackageTests.cs;region=GeoGeoPackage_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var cities = GeoPackage.Read("TestData/places.gpkg", "cities");
var paris = cities[0];
Assert.Equal("1", paris.Id); // The primary key of the table
Assert.Equal(2_102_650, paris.Properties["population"]); // A MEDIUMINT column is an int
Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)paris.Geometry!).Position);

// Write the features in another format, such as GeoJSON
var geoJson = GeoJson.Format(cities);
```
<!-- endref -->

Both methods also take an open `SqliteConnection`, for a database in memory or a connection your application already has.

## Read a large table or a region

[GeoPackage.ReadFeaturesAsync](cref:Meziantou.Framework.Geo.GeoPackage.ReadFeaturesAsync) returns the features one at a
time, as SQLite reads the rows: only the row being read is in memory. Set
[GeoPackageReaderOptions.Bounds](cref:Meziantou.Framework.Geo.GeoPackageReaderOptions.Bounds) to read only the features
whose bounds intersect a box. When the table has an R-tree spatial index, as the tables GDAL writes have, the index finds
the rows to read:

<!-- ref:../../../tests/Meziantou.Framework.Geo.GeoPackage.Tests/GeoPackageTests.cs;region=GeoGeoPackage_ReadFeaturesAsync;format=md-fenced-code-block;dedent=true -->
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

The features are kept whole: a feature that crosses the edge of the box is not cut. Cut them with
[Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip), see [geometry operations](../06-GeometryOperations.md).

## What the reader keeps

- **Geometries**: the standard GeoPackage geometries, read as [WellKnownBinary](cref:Meziantou.Framework.Geo.WellKnownBinary)
  reads them, with their altitudes and measures. An empty or `NULL` geometry gives a feature without
  geometry. The extended geometries of GeoPackage extensions throw a `FormatException`.
- **Identifier**: the [Id](cref:Meziantou.Framework.Geo.GeoFeature.Id) of a feature is the text of the primary key of
  the table, such as `fid`.
- **Properties**: the other columns, with the type their declared type gives: a `bool` for `BOOLEAN`, an `sbyte`,
  `short`, `int` or `long` for `TINYINT`, `SMALLINT`, `MEDIUMINT` and `INTEGER`, a `float` for `FLOAT`, a `double` for
  `DOUBLE` and `REAL`, a `string` for `TEXT`, a `DateOnly` for `DATE`, a `DateTimeOffset` for `DATETIME` and an array
  of bytes for `BLOB`. A `NULL` value is not a property.
- **Not kept**: the coordinate system of the table. The coordinates are expected to be longitudes and latitudes
  (EPSG:4326), and are not converted otherwise.

The reader opens the file in read-only mode and needs the native SQLite library of `Microsoft.Data.Sqlite`, with its
R-tree module to read a region.
