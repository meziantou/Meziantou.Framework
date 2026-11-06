---
title: Encoded polyline
description: Decode and encode the polylines of Google Maps, OSRM and Valhalla routes.
---

The encoded polyline algorithm stores a line as a short string. The routing services return their routes in this
format: Google Maps, OSRM and Valhalla. [EncodedPolyline.Decode](cref:Meziantou.Framework.Geo.EncodedPolyline.Decode)
returns the positions, and [EncodedPolyline.Encode](cref:Meziantou.Framework.Geo.EncodedPolyline.Encode) encodes them:

<!-- ref:../../../tests/Meziantou.Framework.Geo.Tests/EncodedPolylineTests.cs;region=Geo_EncodedPolylineDecode;format=md-fenced-code-block;dedent=true -->
```csharp
// The geometry of a route returned by OSRM or by the Google Maps APIs
var positions = EncodedPolyline.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@");

Assert.Equal(new GeoPosition(-120.2, 38.5), positions[0]);
var route = new GeoLineString(positions);
Assert.Equal(3, route.Positions.Count);

// Valhalla, and OSRM with geometries=polyline6, use 6 decimal digits
var encoded = EncodedPolyline.Encode(route.Positions, precision: 6);
Assert.Equal("_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI", encoded);
```
<!-- endref -->

- The precision is the number of decimal digits of the coordinates: 5 for Google Maps and OSRM, 6 for Valhalla and the
  `polyline6` geometries of OSRM. Decoding with the wrong precision gives positions 10 times too large or too small.
- The format has no altitude: the altitudes are ignored when encoding.
- Build a line with `new GeoLineString(positions)` to compute its length or write it to another format.
