---
title: Getting started
description: Install the package, load the map of a region from a bundle, and read its layers.
---

## Install the package

Add the `Meziantou.Framework.Geo.Maps` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Geo.Maps" />

The package has no map of its own: the maps are the bundles of the regions you need, built by the
[Meziantou.Framework.Geo.Maps.Tool .NET tool](03-GeneratingBundles.md). The examples of this documentation omit the `using` directives: they need
`using Meziantou.Framework.Geo;` and `using Meziantou.Framework.Geo.Maps;`.

## Load the map of a region

A [MapCatalog](cref:Meziantou.Framework.Geo.Maps.MapCatalog) is a folder with a map per region: by default, a subfolder
per region, in one of the folder [formats](04-Formats.md). [MapCatalog.Default](cref:Meziantou.Framework.Geo.Maps.MapCatalog.Default) is the `maps` folder next to the
application, where the packages of bundles copy their regions. [GetMap](cref:Meziantou.Framework.Geo.Maps.MapCatalog.GetMap)
returns the [Map](cref:Meziantou.Framework.Geo.Maps.Map) of a region, and [Load](cref:Meziantou.Framework.Geo.Maps.Map.Load)
reads a layer:

<!-- ref:../../tests/Meziantou.Framework.Geo.Maps.Tests/MapCatalogTests.cs;region=GeoMaps_LoadMap;format=md-fenced-code-block;dedent=true -->
```csharp
// The packages of bundles copy their regions to the maps folder next to the application
var map = MapCatalog.Default.GetMap("Switzerland");
Assert.Equal("Switzerland", map.Name);
Assert.StartsWith("© OpenStreetMap contributors", map.Attribution);

// The layers are read when they are loaded
var cantons = map.Load(MapLayer.StatesProvinces);
Assert.HasCount(26, cantons);
Assert.Contains("Vaud", cantons.Select(canton => (string?)canton.Properties["name"]));

var cities = map.Load(MapLayer.Cities);
var bern = cities.Single(city => city.Properties["name"] is "Bern");
Assert.Equal(true, bern.Properties["capital"]);

// Switzerland has no sea: it has no coastline, land or ocean layer
Assert.False(map.HasLayer(MapLayer.Coastline));
```
<!-- endref -->

A package of a bundle that `pack` builds by default embeds the map in an assembly instead: the map is a static
property of `Map` named after the region, such as `Map.Region_Switzerland`, with the same API as the maps of a catalog.
See [ship the bundles](03-GeneratingBundles.md#ship-the-bundles).

The layers are read when they are loaded: keep the collections you use more than once.
[Layers](cref:Meziantou.Framework.Geo.Maps.Map.Layers) lists the layers of the map, and
[HasLayer](cref:Meziantou.Framework.Geo.Maps.Map.HasLayer) tells whether it has one: a country without sea has no
coastline. [TryGetMap](cref:Meziantou.Framework.Geo.Maps.MapCatalog.TryGetMap) returns `false` for a region that is not
in the catalog, where `GetMap` throws a `KeyNotFoundException`.

The properties that [MapLayer](cref:Meziantou.Framework.Geo.Maps.MapLayer) documents have the same type in every
format: `rank` and `admin_level` are an `int`, `population` a `long` and `capital` a `bool`.

## Load a part of a region

[Load](cref:Meziantou.Framework.Geo.Maps.Map.Load) with a box returns only the features whose bounds intersect it,
such as the cities of the part of a continent that a map shows. With FlatGeobuf and GeoPackage, the spatial index of
the layer finds them without decoding the others; a GeoJSON layer has no index, and is read in full:

<!-- ref:../../tests/Meziantou.Framework.Geo.Maps.Tests/MapCatalogTests.cs;region=GeoMaps_LoadBounds;format=md-fenced-code-block;dedent=true -->
```csharp
var map = MapCatalog.Default.GetMap("Switzerland");

// The cities around Lake Geneva: only the features in the box are decoded
var lakeGeneva = new GeoBounds(West: 5.9, South: 46.1, East: 7.0, North: 46.6);
var cities = map.Load(MapLayer.Cities, lakeGeneva);
Assert.Equal(["Geneva", "Lausanne"], cities.Select(city => (string)city.Properties["name"]!).Order(StringComparer.Ordinal));
```
<!-- endref -->

The features are not cut to the box: [Clip](cref:Meziantou.Framework.Geo.GeoGeometry.Clip) cuts them, see
[geometry operations](../Geo/06-GeometryOperations.md).

## Draw a map

[Maps](../Charts/04-Gallery/09-Maps.md#map-of-a-region) draws the map of a region with `Meziantou.Framework.Charts`,
and [image rendering](../Charts/07-ImageRendering.md) renders it as a PNG image. Show
[Attribution](cref:Meziantou.Framework.Geo.Maps.Map.Attribution) on the maps you publish.
