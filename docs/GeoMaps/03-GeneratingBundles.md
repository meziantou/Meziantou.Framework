---
title: Generating bundles
description: Build the bundles of the world, a continent or a country with the Meziantou.Framework.Geo.Maps.Tool .NET tool, and ship them with an application or as NuGet packages.
---

The bundles are built by the `Meziantou.Framework.Geo.Maps.Tool` .NET tool: it downloads the sources, cuts them to each
region and writes the bundles.

## Install the tool

```bash
dotnet tool install --global Meziantou.Framework.Geo.Maps.Tool
```

## Build the bundles

Run `build` with the regions to build:

```bash
Meziantou.Framework.Geo.Maps.Tool build --region switzerland,france --previews previews
```

A region is `world`, a continent (`africa`, `antarctica`, `asia`, `europe`, `north-america`, `oceania` or
`south-america`), or a country or territory, named by its English name in lowercase words joined by hyphens
(`switzerland`, `united-states`, `cote-divoire`) or by its ISO 3166-1 code (`CH`, `US`). `Meziantou.Framework.Geo.Maps.Tool list`
lists them. The continents and the names come from the regions of [Unicode CLDR](https://cldr.unicode.org/), the UN M49
regions. `--all` builds every region: the world, the continents, and every country and territory that has a border,
overseas territories included.

- `--output` sets the folder of the bundles, `./maps` by default.
- `--format` chooses the [format](04-Formats.md) of the bundles: `fgb` (folders of FlatGeobuf files, the default),
  `geojson` (folders of GeoJSON files) or `gpkg` (GeoPackage files).
- `--layers` writes only some of the layers, such as `--layers borders,states-provinces` for a choropleth that needs no
  cities, rivers or coastline: the bundles are smaller, and a [Map](cref:Meziantou.Framework.Geo.Maps.Map) of such a
  bundle reports only these [Layers](cref:Meziantou.Framework.Geo.Maps.Map.Layers). The bounds of the regions are the
  same whatever the layers.
- `--previews` renders each bundle as an image to a folder, to check what was built.

The first run downloads the sources, about 160 MB, to the application data folder of the user (`--cache` chooses
another folder), and loading them takes about a minute, even for a single region: build the regions you need in one run.

The sources are pinned by SHA-256 in the tool, with the date they were pinned, which is the date of the data of the
maps: a version of the tool builds the same bundles, byte for byte. To build with newer data, `update-pins --pins
pins.json` pins the latest version of every source in a file, and `--pins pins.json` builds with it.

## Ship the bundles

Copy the map of each region, its folder or its GeoPackage file, to a `maps` folder next to the application, where
[MapCatalog.Default](cref:Meziantou.Framework.Geo.Maps.MapCatalog.Default) reads them:

```xml
<ItemGroup>
  <None Include="maps/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Or make a NuGet package per region, for your own feed:

```bash
Meziantou.Framework.Geo.Maps.Tool pack --region europe,france --package-id-prefix Contoso.Maps --package-version 1.0.0
```

`pack` takes the options of `build`, and writes the packages to `./packages` (`--package-output`). A package is named
after the prefix and the key of the region, such as `Contoso.Maps.France`, and `--authors` sets its authors. The bundles
are not published on nuget.org.

By default, a package holds an assembly that embeds the map, built by the .NET SDK: `pack` needs the .NET SDK, and
builds the assemblies of all the regions at the end, in one build. A project that references the package opens the map
with a static property of [Map](cref:Meziantou.Framework.Geo.Maps.Map) named after the key of the region, such as
`Map.Region_France`, in the namespace of the prefix (`using Contoso.Maps;`). It is a C# 14 extension member: the
project needs C# 14, the default of .NET 10. No file is copied next to the application, so the map works in a
single-file or a Native AOT application, and in tests whatever their folder. The layers are read from the memory of the
loaded assembly, without copying them first, and a [Load](cref:Meziantou.Framework.Geo.Maps.Map.Load) with a box still
uses the spatial index of the FlatGeobuf layers.

`--package-content files` makes the packages of the previous versions instead: their targets copy the region to the
`maps` folder of the output of the projects that reference them, where
[MapCatalog.Default](cref:Meziantou.Framework.Geo.Maps.MapCatalog.Default) reads it. A GeoPackage bundle needs these
packages, because SQLite reads a file: such a package depends on `Meziantou.Framework.Geo.Maps.GeoPackage`, and needs a
catalog that reads the format, see [read GeoPackage files](04-Formats.md#read-geopackage-files).

The bundle of a country is from a few kilobytes to a few megabytes, and the bundle of the world, which has every
subdivision, is about 100 MB. Bundle only the regions the application draws.

## Licenses

The borders and subdivisions are derived from OpenStreetMap: the bundles are under the
[Open Database License](https://opendatacommons.org/licenses/odbl/), and the maps made from them must credit
"© OpenStreetMap contributors", the text of [Map.Attribution](cref:Meziantou.Framework.Geo.Maps.Map.Attribution). The
packages that `pack` writes have the `ODbL-1.0` license.
