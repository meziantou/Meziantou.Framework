---
title: Introduction
description: What map bundles are, the layers they hold, and where their data comes from.
---

`Meziantou.Framework.Geo.Maps` loads map bundles: the map of a region, the world, a continent or a country, stored next
to the application, so that an application draws maps without a map server or a network connection.

## What a bundle holds

A bundle is the map of a region in its layers:

| Layer | Geometries | Properties |
|---|---|---|
| [Borders](cref:Meziantou.Framework.Geo.Maps.MapLayer.Borders) | polygons of the countries | `name`, `iso` (ISO 3166-1 alpha-2) |
| [StatesProvinces](cref:Meziantou.Framework.Geo.Maps.MapLayer.StatesProvinces) | polygons of the subdivisions | `name`, `iso` (ISO 3166-2), `country`, `admin_level` |
| [Cities](cref:Meziantou.Framework.Geo.Maps.MapLayer.Cities) | points of the populated places | `name`, `country`, `population`, `rank` (0 to 14), `capital` |
| [Rivers](cref:Meziantou.Framework.Geo.Maps.MapLayer.Rivers) | lines | `name` |
| [Lakes](cref:Meziantou.Framework.Geo.Maps.MapLayer.Lakes) | polygons | `name` |
| [Coastline](cref:Meziantou.Framework.Geo.Maps.MapLayer.Coastline) | lines | |
| [Land](cref:Meziantou.Framework.Geo.Maps.MapLayer.Land) | polygons cut to the bounds of the region | |
| [Ocean](cref:Meziantou.Framework.Geo.Maps.MapLayer.Ocean) | polygons cut to the bounds of the region | |
| [MarineAreas](cref:Meziantou.Framework.Geo.Maps.MapLayer.MarineAreas) | polygons of the seas, gulfs, bays and straits, cut to the bounds of the region | `name`, `kind` (`sea`, `gulf`, `bay`, `strait`...), `scale_rank` (0 to 9) |
| [PhysicalRegions](cref:Meziantou.Framework.Geo.Maps.MapLayer.PhysicalRegions) | polygons of the mountain ranges, deserts, plateaus, islands..., cut to the bounds of the region, and points of the capes and waterfalls | `name`, `kind` (`range/mtn`, `desert`, `island`, `cape`...), `scale_rank` (0 to 7) |

The `scale_rank` of the marine areas and physical regions is the importance Natural Earth gives them, from 0 for the
most important: label the smallest ranks first, such as the seas and the mountain ranges of rank 0 to 3 on the map of a
country. The `rank` of the cities goes the other way, from 0 to 14 for the largest places.

A layer is missing when its source has nothing in the region: a country without sea has no coastline, land or ocean.
Antarctica has no borders or subdivisions: its map has the research stations as cities, and the land, coastline, ocean,
seas and physical regions south of 60°S. Draw it with a projection centered on the South Pole, such as
[AzimuthalEquidistantProjection](cref:Meziantou.Framework.Geo.AzimuthalEquidistantProjection): see
[map of a polar region](../Charts/04-Gallery/09-Maps.md#map-of-a-polar-region).
The metadata of the map holds the name of the region, its bounds, the date of its data and the attribution of its
sources. The positions are longitudes and latitudes (WGS 84), and the layers are read as the features of
[Meziantou.Framework.Geo](../Geo/01-Introduction.md).

A bundle is stored in one of three [formats](04-Formats.md): a folder with a
[FlatGeobuf](../Geo/04-Formats/09-FlatGeobuf.md) file per layer (the default), a folder with a GeoJSON file per layer,
or one GeoPackage file. The API is the same in every format.

## Where the data comes from

The borders and subdivisions come from [OpenStreetMap](https://www.openstreetmap.org/), through
[country-levels](https://github.com/hyperknot/country-levels): a bundle is under the
[Open Database License](https://opendatacommons.org/licenses/odbl/), and the maps made from it must credit
"© OpenStreetMap contributors". [Map.Attribution](cref:Meziantou.Framework.Geo.Maps.Map.Attribution) holds the text to
show. The cities, rivers, lakes, coastline, land, ocean, marine areas and physical regions come from
[Natural Earth](https://www.naturalearthdata.com/)
at 1:10m, in the public domain: they are generalized for maps of a country or larger, not for maps of a city.
The continents group the countries as the [Unicode CLDR](https://cldr.unicode.org/) regions do: North America includes
Central America and the Caribbean, and Europe includes Russia.

## Next steps

- [Getting started](02-GettingStarted.md): load the map of a region and its layers.
- [Generating bundles](03-GeneratingBundles.md): build the bundles of the regions you need.
- [Formats](04-Formats.md): choose how the bundles are stored, read GeoPackage files, and convert a bundle.
- [Maps](../Charts/04-Gallery/09-Maps.md#map-of-a-region): draw the map of a region with `Meziantou.Framework.Charts`.
