# Meziantou.Framework.Geo.GeoParquet

Reads GeoParquet files (`.parquet`), the columnar format that GDAL, GeoPandas, DuckDB and the cloud data catalogs write,
as the features of `Meziantou.Framework.Geo`. The file is read one row group at a time, and the row groups outside of a
region are skipped when the file has a bbox covering column.

## Usage

<!-- ref:../../tests/Meziantou.Framework.Geo.GeoParquet.Tests/GeoParquetTests.cs;region=GeoGeoParquet_ReadFeaturesAsync;format=md-fenced-code-block;dedent=true -->
```csharp
// The file is read one row group at a time; with a box, the row groups outside of it are skipped when the file
// has a bbox covering column
var northAmerica = new GeoBounds(West: -130, South: 25, East: -60, North: 60);
var names = new List<string?>();
await foreach (var country in GeoParquet.ReadFeaturesAsync("TestData/example.parquet", new GeoParquetReaderOptions { Bounds = northAmerica }))
{
    names.Add((string?)country.Properties["name"]);
}

Assert.Equal(["Canada", "United States of America"], names);
```
<!-- endref -->

The package reads the Parquet files itself, with no dependency outside .NET. Only the geometry columns encoded as WKB are
read, and the files compressed with Zstandard need .NET 11 or later.
