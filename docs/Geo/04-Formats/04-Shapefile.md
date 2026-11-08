---
title: Shapefile
description: Read shapefiles and their attribute tables, from files or zip archives, and write them.
---

A shapefile is the format of GIS software and of the open data of public agencies. It is a set of files with the same
name: `.shp` holds the geometries, `.dbf` the attributes, `.shx` an index, `.cpg` the encoding of the attributes and
`.prj` the coordinate system. They are often downloaded as a zip file.

## Read a shapefile

[Shapefile.Read](cref:Meziantou.Framework.Geo.Shapefile.Read) reads the `.shp` file and the `.dbf`, `.cpg` and `.prj`
files next to it:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/ShapefileTests.cs;region=Geo_ShapefileRead;format=md-fenced-code-block;dedent=true -->
```csharp
// Reads points.shp with the attributes of points.dbf, decoded with the encoding of points.cpg
var features = Shapefile.Read("TestData/shapefile/points.shp");
Assert.Equal("points", features.Name);
var paris = features[0];
Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)paris.Geometry!).Position);
Assert.Equal("Paris", paris.Properties["name"]);
Assert.Equal(2_102_650.0, paris.Properties["population"]); // Numbers are doubles
Assert.Equal(true, paris.Properties["capital"]);
Assert.Equal(new DateOnly(1952, 1, 31), paris.Properties["founded"]);
```
<!-- endref -->

[Shapefile.ReadZip](cref:Meziantou.Framework.Geo.Shapefile.ReadZip) reads a zip file. When the archive holds several
shapefiles, give the name of the one to read:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/ShapefileTests.cs;region=Geo_ShapefileReadZip;format=md-fenced-code-block;dedent=true -->
```csharp
// A shapefile downloaded as a .zip archive
using var zip = File.OpenRead("TestData/shapefile/countries.zip");
var features = Shapefile.ReadZip(zip);
Assert.Equal("countries", features.Name);
var polygon = (GeoPolygon)features[0].Geometry!;
Assert.HasCount(2, polygon.Rings); // The exterior ring and a hole
Assert.True(polygon.Contains(new GeoPosition(5, 5)));
```
<!-- endref -->

- The [Name](cref:Meziantou.Framework.Geo.GeoFeatureCollection.Name) of the collection is the name of the file.
- The geometries are points, multipoints, lines and polygons, with their Z values as altitudes; the M values
  (measures) are dropped. The rings of the polygons are grouped into polygons and holes by their orientation, and by
  their nesting when the file has the wrong orientation.
- The attributes are a `string`, a `double`, a `bool` or a `DateOnly`, by the type of their field. An empty value is
  `null`. Deleted records are skipped. The features have no [Id](cref:Meziantou.Framework.Geo.GeoFeature.Id).
- The encoding of the attributes comes from the `.cpg` file, or else from the header of the `.dbf` file. UTF-8,
  ISO-8859-1 and Windows-1252 are always supported; the other code pages need a provider, such as
  `CodePagesEncodingProvider`, registered by your application. Set
  [Encoding](cref:Meziantou.Framework.Geo.ShapefileReaderOptions.Encoding) when a file declares a wrong encoding.
- The `.prj` file gives the
  [CoordinateReferenceSystem](cref:Meziantou.Framework.Geo.GeoFeatureCollection.CoordinateReferenceSystem) of the
  features, or `null` when there is none or it is not supported. The positions are those of the file, in meters for a
  shapefile in UTM or Lambert 93: set [TransformToWgs84](cref:Meziantou.Framework.Geo.ShapefileReaderOptions.TransformToWgs84)
  to convert them to longitudes and latitudes. See
  [coordinate reference systems](../05-Projections.md#read-the-coordinate-reference-system-of-a-file).

## Write a shapefile

[Shapefile.Write](cref:Meziantou.Framework.Geo.Shapefile.Write) writes the `.shp`, `.shx`, `.dbf`, `.cpg` (UTF-8) and
`.prj` (WGS 84) files, and [Shapefile.WriteZip](cref:Meziantou.Framework.Geo.Shapefile.WriteZip) writes them to a zip
file:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/ShapefileTests.cs;region=Geo_ShapefileWrite;format=md-fenced-code-block;dedent=true -->
```csharp
var features = new GeoFeatureCollection
{
    new(new GeoPoint(new GeoPosition(2.3522, 48.8566)), new Dictionary<string, object?> { ["name"] = "Paris", ["population"] = 2_102_650 }),
    new(new GeoPoint(new GeoPosition(-0.5792, 44.8378)), new Dictionary<string, object?> { ["name"] = "Bordeaux", ["population"] = 261_804 }),
};

// Writes cities.shp, cities.shx, cities.dbf, cities.cpg and cities.prj
var directory = Directory.CreateTempSubdirectory();
Shapefile.Write(features, Path.Combine(directory.FullName, "cities.shp"));

// Or a .zip archive of these files
using var zip = new MemoryStream();
Shapefile.WriteZip(features, zip, "cities");
```
<!-- endref -->

- A shapefile holds one type of geometry: points, lines or polygons. Mixing them, or writing a
  [GeoGeometryCollection](cref:Meziantou.Framework.Geo.GeoGeometryCollection), throws an `ArgumentException`. Write
  each type to its own shapefile.
- When a position has an altitude, every position is written with one, and 0 for those that have none.
- The fields are the properties of the features. Their names are cut to 10 characters and made unique, as GDAL does.
  The text values are cut to 254 bytes, before a UTF-8 sequence that does not fit whole. A field whose values have different types is written as text.
- The identifiers are not written: shapefiles have none.
- The overload that writes to streams does not write the `.cpg` file: write `UTF-8` to it yourself, so other programs
  read the attributes with the right encoding.
