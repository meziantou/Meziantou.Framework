---
title: Getting started
description: Install the package, read a GeoJSON file, filter its features and write them back.
---

## Install the package

Add the `Meziantou.Framework.Geo` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Geo" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Geo;`.

## Read and write a file

Every format has a static class with the same methods: `Read` reads a stream, `Parse` reads a string, `Write` writes
to a stream and `Format` returns a string. This example reads the countries of a GeoJSON file with
[GeoJson.Read](cref:Meziantou.Framework.Geo.GeoJson.Read), keeps the large ones and writes them to another file with
[GeoJson.Write](cref:Meziantou.Framework.Geo.GeoJson.Write):

<!-- ref:../../tests/Meziantou.Framework.Geo.Tests/GeoJsonTests.cs;region=Geo_GeoJsonReadWrite;format=md-fenced-code-block;dedent=true -->
```csharp
GeoFeatureCollection countries;
using (var stream = File.OpenRead("countries.geojson"))
{
    countries = GeoJson.Read(stream);
}

// A JSON number is a double, and a string is a string
var large = countries.Where(country => (double)country.Properties["population"]! > 50).ToList();
Assert.Equal("France", Assert.Single(large).Properties["name"]);

using (var stream = File.Create("large-countries.geojson"))
{
    GeoJson.Write(new GeoFeatureCollection(large), stream, new GeoJsonWriterOptions { Indented = true });
}
```
<!-- endref -->

- A reader returns a [GeoFeatureCollection](cref:Meziantou.Framework.Geo.GeoFeatureCollection): a list of
  [GeoFeature](cref:Meziantou.Framework.Geo.GeoFeature), each with a geometry, properties and an identifier.
- The readers and writers do not own the streams: dispose them yourself.
- Invalid data throws a `FormatException`.

To convert a file to another format, read it with one class and write it with another: see
[formats](04-Formats/01-Overview.md).

## Next steps

- [Features and geometries](03-FeaturesAndGeometries.md): the model, bounds, distances and point-in-polygon tests.
- [Formats](04-Formats/01-Overview.md): what each format keeps and how to convert between them.
- [Projections](05-Projections.md): turn positions into points to draw a map.
