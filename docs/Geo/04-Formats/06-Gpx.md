---
title: GPX
description: Read the waypoints, routes and tracks of GPS devices and sport apps, compute the length of a track, and write GPX files.
---

[GPX](https://www.topografix.com/gpx.asp) is the XML format of GPS devices and sport apps.
[Gpx](cref:Meziantou.Framework.Geo.Gpx) reads GPX 1.0 and 1.1, and writes GPX 1.1. This example reads an activity and
computes the length of its track:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/GpxTests.cs;region=Geo_GpxTrackLength;format=md-fenced-code-block;dedent=true -->
```csharp
GeoFeatureCollection features;
using (var stream = File.OpenRead("activity.gpx"))
{
    features = Gpx.Read(stream);
}

// A track is a multi-line string, with one line per segment
var track = features.Single(feature => (string?)feature.Properties[Gpx.TypePropertyName] is "trk");
var segments = ((GeoMultiLineString)track.Geometry!).LineStrings;
var length = segments.Sum(segment => segment.GetLength());

Assert.Equal("Lunch Run", track.Properties["name"]);
Assert.Equal(173, Math.Round(length, MidpointRounding.AwayFromZero));
```
<!-- endref -->

## What the reader keeps

Each waypoint, route and track is a feature, in the order of the document:

| GPX | Geometry |
|---|---|
| `wpt`, a waypoint | [GeoPoint](cref:Meziantou.Framework.Geo.GeoPoint) |
| `rte`, a route | [GeoLineString](cref:Meziantou.Framework.Geo.GeoLineString) |
| `trk`, a track | [GeoMultiLineString](cref:Meziantou.Framework.Geo.GeoMultiLineString), a line per segment, even when it has one |

- The elevations (`ele`) are the altitudes. The name of the file (`metadata/name`) is the
  [Name](cref:Meziantou.Framework.Geo.GeoFeatureCollection.Name) of the collection.
- The property named by [Gpx.TypePropertyName](cref:Meziantou.Framework.Geo.Gpx.TypePropertyName), `gpxType`, is
  `wpt`, `rte` or `trk`.
- The other properties are `name`, `cmt`, `desc`, `src`, `type` and `sym` as strings, the `number` of a route or a
  track as a `double`, and the `time` of a waypoint as a `DateTimeOffset`.
- The points of the routes and tracks keep their position and elevation only: their time, and the data of the
  `extensions` such as the heart rate, are dropped. The links and the other data of the waypoints are dropped too.

## Write a GPX file

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/GpxTests.cs;region=Geo_GpxWrite;format=md-fenced-code-block;dedent=true -->
```csharp
GeoFeatureCollection features =
[
    // A point is a waypoint, and a multi-line string is a track with one segment per line
    new GeoFeature(new GeoPoint(new GeoPosition(6.8652, 45.8326, 4808)), new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["name"] = "Mont Blanc",
        ["time"] = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero),
    }),
    new GeoFeature(new GeoMultiLineString([new GeoLineString([new(6.8693, 45.9237, 1035), new(6.8814, 45.8775, 2372)])]), new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["name"] = "Ascent",
    }),
];
features.Name = "Mont Blanc";

using (var stream = File.Create("mont-blanc.gpx"))
{
    Gpx.Write(features, stream, new GpxWriterOptions { Creator = "My application", Indented = true });
}
```
<!-- endref -->

- A point is a waypoint, a line is a route, and a multi-line is a track. Set `gpxType` to `trk` to write a line as a
  track of one segment.
- As GPX requires, the waypoints come first, then the routes, then the tracks.
- The writer writes the properties the reader reads; the other properties and the identifiers are dropped, as GPX has
  no element for them.
- GPX has no polygons: writing a polygon, a collection or a feature without a geometry throws an `ArgumentException`.
- [GpxWriterOptions](cref:Meziantou.Framework.Geo.GpxWriterOptions) sets the
  [Creator](cref:Meziantou.Framework.Geo.GpxWriterOptions.Creator) of the file and whether it is
  [Indented](cref:Meziantou.Framework.Geo.GpxWriterOptions.Indented).
