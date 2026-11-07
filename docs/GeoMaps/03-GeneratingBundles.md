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
after the prefix and the key of the region, such as `Contoso.Maps.France`, and `--authors` sets its authors. It depends
on `Meziantou.Framework.Geo.Maps`, or on `Meziantou.Framework.Geo.Maps.GeoPackage` for a GeoPackage bundle, and its
targets copy the region to the `maps` folder of the output of the projects that reference it. A GeoPackage bundle needs
a catalog that reads the format: see [read GeoPackage files](04-Formats.md#read-geopackage-files). The bundles are not
published on nuget.org.

The bundle of a country is from a few kilobytes to a few megabytes, and the bundle of the world, which has every
subdivision, is about 100 MB. Bundle only the regions the application draws.

## Licenses

The borders and subdivisions are derived from OpenStreetMap: the bundles are under the
[Open Database License](https://opendatacommons.org/licenses/odbl/), and the maps made from them must credit
"© OpenStreetMap contributors", the text of [Map.Attribution](cref:Meziantou.Framework.Geo.Maps.Map.Attribution). The
packages that `pack` writes have the `ODbL-1.0` license.
