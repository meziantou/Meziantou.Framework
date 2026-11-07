---
title: KML and KMZ
description: Read the placemarks of Google Earth and Google My Maps files, and write KML and KMZ documents.
---

[KML](https://www.ogc.org/standards/kml/) is the XML format of Google Earth and Google My Maps, and KMZ is a zip file
that holds a KML document and its images. [Kml](cref:Meziantou.Framework.Geo.Kml) reads and writes KML, and
[Kmz](cref:Meziantou.Framework.Geo.Kmz) reads and writes KMZ:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/KmlTests.cs;region=Geo_KmlReadWrite;format=md-fenced-code-block;dedent=true -->
```csharp
GeoFeatureCollection placemarks;
using (var stream = File.OpenRead("trip.kml"))
{
    placemarks = Kml.Read(stream);
}

// The placemarks of every folder, in order; the "folder" property gives the folder of each one
Assert.Equal("Trip", placemarks.Name);
var monuments = placemarks.Where(placemark => (string?)placemark.Properties.GetValueOrDefault("folder") == "Monuments").ToList();
var eiffelTower = Assert.Single(monuments);
Assert.Equal("Eiffel Tower", eiffelTower.Properties["name"]);
Assert.Equal("<b>330</b> m", eiffelTower.Properties["description"]);
Assert.Equal("1889", eiffelTower.Properties["year"]); // KML has no types: ExtendedData values are strings

using (var stream = File.Create("monuments.kml"))
{
    Kml.Write(new GeoFeatureCollection(monuments) { Name = "Monuments" }, stream, new KmlWriterOptions { Indented = true });
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/KmlTests.cs;region=Geo_KmzReadWrite;format=md-fenced-code-block;dedent=true -->
```csharp
using (var stream = File.Create("trip.kmz"))
{
    Kmz.Write(placemarks, stream);
}

using (var stream = File.OpenRead("trip.kmz"))
{
    var copy = Kmz.Read(stream);
    Assert.HasCount(2, copy);
}
```
<!-- endref -->

## What the reader keeps

- Every `Placemark`, in the folders at any depth, in the order of the document. The name of the document becomes the
  [Name](cref:Meziantou.Framework.Geo.GeoFeatureCollection.Name) of the collection, and the `id` of a placemark its
  [Id](cref:Meziantou.Framework.Geo.GeoFeature.Id).
- The properties: `name`, `description` (its HTML is kept as text), the items of `ExtendedData`, and `folder`, the
  names of the folders that contain the placemark, separated by `/`. Google My Maps puts each layer in a folder.
- KML has no types: the values are strings, except the `SimpleData` items that a `Schema` declares as numbers
  (`double`) or booleans (`bool`).
- The geometries: points, lines, polygons, `MultiGeometry`, and the `gx:Track` and `gx:MultiTrack` tracks of Google
  Earth, as lines. A `LinearRing` alone is a line, as Google Earth draws it. The altitudes are kept as they are
  written, whatever their `altitudeMode`.
- The styles, the times, the 3D models, the overlays and the network links are ignored: nothing is downloaded.
- A KMZ file is read from its `doc.kml` entry, or else its first `.kml` entry. The other files are ignored.

## What the writer writes

- A KML 2.2 document with a `Placemark` per feature, also for the features without a geometry.
- The `name` and `description` of a placemark come from the string properties named by
  [NameProperty](cref:Meziantou.Framework.Geo.KmlWriterOptions.NameProperty) and
  [DescriptionProperty](cref:Meziantou.Framework.Geo.KmlWriterOptions.DescriptionProperty) (`name` and `description`
  by default). The other properties are `ExtendedData` items, as text.
- The identifier is written when it is a valid XML identifier: a number, such as the `id` of a GeoJSON feature, is
  not.
- A geometry with altitudes gets the `absolute` altitude mode; otherwise, Google Earth draws it on the ground.
- The folders are not written back: the `folder` property is an `ExtendedData` item like the others.
