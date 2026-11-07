---
title: Generating bundles
description: Use the published bundles of the world, a continent or a country, or build them with the tool of the repository, and ship them with an application or as NuGet packages.
---

The bundles are built by `Meziantou.Framework.Geo.Maps.Tool`, a command-line tool of the repository: it downloads the
sources, cuts them to each region and writes the bundles. The bundle of every region is published on nuget.org, built by
this tool; run it yourself for another format, fewer layers, other data or your own feed.

## Use the published bundles

The bundle of every region is published on nuget.org, as a package named after the key of the region:
`Meziantou.Framework.Geo.Maps.Data.World`, `Meziantou.Framework.Geo.Maps.Data.Europe`,
`Meziantou.Framework.Geo.Maps.Data.France`... The `list` command of the tool lists the keys. A package holds an
assembly that embeds the map, and opens it with a static property of [Map](cref:Meziantou.Framework.Geo.Maps.Map)
named after the key of the region, in the `Meziantou.Framework.Geo.Maps.Data` namespace: with
`using Meziantou.Framework.Geo.Maps.Data;`, `Map.Region_Switzerland` is the map of Switzerland, whose layers
[Load](cref:Meziantou.Framework.Geo.Maps.Map.Load) reads as with any other map.

<InstallNuGetPackage Id="Meziantou.Framework.Geo.Maps.Data.Switzerland" />

`Map.Region_Switzerland` is a C# 14 extension member: the project needs C# 14, the default of .NET 10. The published
bundles have every layer, in the FlatGeobuf format, and target the oldest .NET version the libraries support,
`net10.0` today. The sources are checked every week, and a new version of a bundle is published only when its map
changes: its version starts at `1.0.0` and its last number is incremented on each change, so the bundles of two regions
can have different versions. The date of the data of a map is
[Metadata.LastModified](cref:Meziantou.Framework.Geo.Maps.MapMetadata.LastModified). The projects of the packages are in the repository, in
[src/Meziantou.Framework.Geo.Maps.Data](https://github.com/meziantou/Meziantou.Framework/tree/main/src/Meziantou.Framework.Geo.Maps.Data),
with the SHA-256 of each layer. Build the bundles yourself for another format, fewer layers, other data or your own
feed.

## Run the tool

The tool is not published: run it from a clone of the repository, with the .NET SDK of its `global.json` file.

```bash
git clone https://github.com/meziantou/Meziantou.Framework.git
cd Meziantou.Framework
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- list
```

The commands below run from the root of the clone. `--output`, `--previews` and the other paths are relative to it.

## Build the bundles

Run `build` with the regions to build:

```bash
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- build --region switzerland,france --previews previews
```

A region is `world`, a continent (`africa`, `antarctica`, `asia`, `europe`, `north-america`, `oceania` or
`south-america`), or a country or territory, named by its English name in lowercase words joined by hyphens
(`switzerland`, `united-states`, `cote-divoire`) or by its ISO 3166-1 code (`CH`, `US`). The `list` command lists
them. The continents and the names come from the regions of [Unicode CLDR](https://cldr.unicode.org/), the UN M49
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
maps: a commit of the repository builds the same bundles, byte for byte. To build with newer data, `update-pins --pins
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
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- pack --region europe,france --package-id-prefix Contoso.Maps --package-version 1.0.0
```

`pack` takes the options of `build`, and writes the packages to `./packages` (`--package-output`). A package is named
after the prefix and the key of the region, such as `Contoso.Maps.France`, and `--authors` sets its authors.

By default, a package holds an assembly that embeds the map, built by the .NET SDK: `pack` needs the .NET SDK, and
builds the assemblies of all the regions at the end, in one build. A project that references the package opens the map
with a static property of [Map](cref:Meziantou.Framework.Geo.Maps.Map) named after the key of the region, such as
`Map.Region_France`, in the namespace of the prefix (`using Contoso.Maps;`). It is a C# 14 extension member: the
project needs C# 14, the default of .NET 10. No file is copied next to the application, so the map works in a
single-file or a Native AOT application, and in tests whatever their folder. The layers are read from the memory of the
loaded assembly, without copying them first, and a [Load](cref:Meziantou.Framework.Geo.Maps.Map.Load) with a box still
uses the spatial index of the FlatGeobuf layers.

The assemblies target `net10.0` and are built with `Microsoft.NET.Sdk`, in a temporary folder that ignores the MSBuild
files of the folders above it. To build them as the projects of your repository:

- `--package-target-framework` sets the target frameworks, `net10.0` or later, separated by semicolons. It can be an
  MSBuild property, such as `--package-target-framework '$(LatestTargetFrameworks)'`, that a `Directory.Build.props`
  file defines. The package has an assembly per target framework.
- `--package-sdk` sets the MSBuild SDK of the projects, such as `Meziantou.NET.Sdk`. The `global.json` file of the
  folders above the projects gives the version of an SDK that is a NuGet package.
- `--package-project-output` writes the projects to a folder, such as a folder of your repository, and keeps them: they
  import the `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` files of the folders
  above them.

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
