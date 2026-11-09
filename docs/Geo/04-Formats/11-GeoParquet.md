---
title: GeoParquet
description: Read GeoParquet files, the columnar format of geospatial data lakes, one row group at a time, and skip the row groups outside of a region.
---

[GeoParquet](https://geoparquet.org/) stores features in Apache Parquet files: a column holds the geometries as WKB,
and the other columns hold the properties. GDAL, GeoPandas, DuckDB and the cloud data catalogs write it for large
datasets. The reader is in the `Meziantou.Framework.Geo.GeoParquet` package, which reads Parquet files itself, with no
dependency outside .NET:

```shell
dotnet add package Meziantou.Framework.Geo.GeoParquet
```

## Read a file

[GeoParquet.ReadAsync](cref:Meziantou.Framework.Geo.GeoParquet.ReadAsync) reads a file or a stream. The stream must be
seekable: the metadata of a Parquet file is at its end.

<!-- ref:../../../tests/Meziantou.Framework.Geo.GeoParquet.Tests/GeoParquetTests.cs;region=GeoGeoParquet_ReadAsync;format=md-fenced-code-block;dedent=true -->
```csharp
var countries = await GeoParquet.ReadAsync("TestData/example.parquet");
var canada = countries.First(country => (string?)country.Properties["name"] is "Canada");
Assert.Equal("North America", canada.Properties["continent"]);

// Write the features in another format, such as FlatGeobuf
var flatGeobuf = FlatGeobuf.Write(countries);
```
<!-- endref -->

## Read a large file or a region

A Parquet file is split into row groups. [GeoParquet.ReadFeaturesAsync](cref:Meziantou.Framework.Geo.GeoParquet.ReadFeaturesAsync)
reads the columns of one row group, returns its features, then reads the next one: the memory holds a row group, not
the file. Set [GeoParquetReaderOptions.Bounds](cref:Meziantou.Framework.Geo.GeoParquetReaderOptions.Bounds) to read only
the features whose bounds intersect a box. When the file has a bbox covering column (GeoParquet 1.1), the reader skips
the row groups whose statistics show that they are outside of the box:

<!-- ref:../../../tests/Meziantou.Framework.Geo.GeoParquet.Tests/GeoParquetTests.cs;region=GeoGeoParquet_ReadFeaturesAsync;format=md-fenced-code-block;dedent=true -->
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

The features are kept whole: a feature that crosses the edge of the box is not cut. Cut them with
[Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip), see [geometry operations](../06-GeometryOperations.md).

## What the reader keeps

- **Geometries**: the primary geometry column of the `geo` metadata, or the one that
  [GeometryColumn](cref:Meziantou.Framework.Geo.GeoParquetReaderOptions.GeometryColumn) names, read as
  [WellKnownBinary](cref:Meziantou.Framework.Geo.WellKnownBinary) reads it, with the altitudes and the
  measures. A `null` or empty geometry gives a feature without geometry. The native (GeoArrow) encodings throw a
  `FormatException`: only WKB is read.
- **Properties**: the other top-level columns, with the .NET type of the column, such as an `int`, a `long`, a
  `double`, a `string`, a `DateTime` or an array of bytes. A `null` value is not a property. The columns of structures,
  lists and maps, such as the bbox covering column, are not read.
- **Not kept**: the identifiers, as GeoParquet has none, and the coordinate system. The coordinates are expected to be
  longitudes and latitudes (OGC:CRS84, the default of GeoParquet), and are not converted otherwise.

## Supported Parquet files

The reader reads the files that GDAL, GeoPandas (pyarrow), DuckDB and Spark write:

- **Compression**: none, Snappy, GZIP, Brotli, LZ4 and Zstandard, on every target framework. LZO is not supported.
- **Pages and encodings**: data pages of versions 1 and 2, dictionaries, and the PLAIN, RLE, DELTA_BINARY_PACKED,
  DELTA_LENGTH_BYTE_ARRAY, DELTA_BYTE_ARRAY and BYTE_STREAM_SPLIT encodings.
- **Types**: the integers, signed or not, the floating-point numbers, the booleans, the strings, the byte arrays, the
  decimals (a `decimal`, or a `double` beyond 28 digits), the dates (`DateOnly`), the times (`TimeOnly`), the
  timestamps (`DateTime`, in UTC when the column is adjusted to UTC) and the UUIDs (`Guid`).
- **Not supported**: encrypted files and column chunks stored in other files throw a `NotSupportedException`.

Invalid data throws a `FormatException`.
