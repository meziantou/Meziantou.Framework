# Meziantou.Framework.Geo.Maps.Tool

A .NET tool that generates the offline map bundles `Meziantou.Framework.Geo.Maps` reads: for the world, each continent
and each country or territory, the borders, subdivisions, cities, rivers, lakes, coastline, land, ocean, seas and
physical regions, as folders of FlatGeobuf or GeoJSON files, GeoPackage files, or NuGet packages of assemblies that
embed them.

```bash
dotnet tool install --global Meziantou.Framework.Geo.Maps.Tool
Meziantou.Framework.Geo.Maps.Tool build --region switzerland,france --previews previews
Meziantou.Framework.Geo.Maps.Tool pack --region europe --package-id-prefix Contoso.Maps --package-version 1.0.0
Meziantou.Framework.Geo.Maps.Tool list
```

`build` writes the bundles to `./maps`, where `MapCatalog` reads them when they are next to the application. `pack` also
writes a NuGet package per region to `./packages`, named after the prefix and the key of the region
(`Contoso.Maps.Europe`), for your own feed: an assembly, built with the .NET SDK, that embeds the map and opens it with
`Map.Region_Europe` (a C# 14 extension member, in the `Contoso.Maps` namespace). `--package-content files` makes
packages that copy the map to the `maps` folder next to the application instead, as GeoPackage bundles need. `list` lists the regions: the world, the continents, and every country and
territory, by their English name in lowercase words joined by hyphens, their key or their ISO code.

The first run downloads the sources, about 160 MB, to the application data folder of the user (`--cache` to choose
another folder), and loads them in about a minute, even for a single region. The next runs read them from the cache.

## Sources

The sources are pinned by SHA-256 in the tool: a version of the tool always builds the same bundles, byte for byte.
`update-pins --pins <file>` pins the latest version of every source in a file, and `--pins <file>` builds with it.

| Layers | Source | License |
| --- | --- | --- |
| Borders, StatesProvinces | [country-levels](https://github.com/hyperknot/country-levels) v2.2.0 (detailed borders, medium subdivisions), from OpenStreetMap | ODbL 1.0 |
| StatesProvinces | OpenStreetMap relations, embedded in the tool: the subdivisions country-levels misses, a quarter of Algeria (7 of the wilayas of 2019) and a tenth of the DR Congo (2 provinces of 2015). Two other wilayas of 2019 have a wrong code in country-levels, which the tool corrects (`DZ-54`, `DZ-57`) | ODbL 1.0 |
| Cities, Rivers, Lakes, Land, Ocean, Coastline, MarineAreas, PhysicalRegions | [Natural Earth](https://www.naturalearthdata.com/) 1:10m, from the [natural-earth-vector](https://github.com/nvkelso/natural-earth-vector) repository at a pinned commit | Public domain |
| The regions | The territory containment (the UN M49 regions) and the English names of [Unicode CLDR](https://github.com/unicode-org/cldr-json), at a pinned release | Unicode License v3 |

The borders and subdivisions are a database derived from OpenStreetMap: the bundles are under the ODbL, and the maps
made from them must credit "© OpenStreetMap contributors". The metadata of each bundle holds the attribution and the
sources.

## How a region is built

1. The ISO 3166-1 codes of the region: the country itself, the countries of a continent, or every country for the world.
   The countries are those country-levels has a border for; CLDR gives their continent and their English name. The
   extent of a continent leaves out a few members that would stretch it: Russia for Europe, and remote islands for
   Africa and South America. Their borders are still in the layers. Antarctica has no border: its extent is fixed, from
   60°S, the limit of the Antarctic Treaty, to the pole, and the extent of the world includes it.
2. The borders and subdivisions of these countries, repaired once when they are loaded (`GeoGeometry.MakeValid`). The
   build fails when a country has a subdivision code twice.
3. The cities of these countries, and the rivers, lakes, marine areas, physical regions, land, ocean and coastline cut to
   the bounds of the borders (`GeoGeometry.Clip`). A region without ocean in its bounds, such as Switzerland, has no
   land, ocean or coastline layer.
4. The map is written in the format of `--format` (`MapFormat.Write`). The date of the data is the date of the pins.

## Maintaining the tool

- `data-versions.json` holds the pins of the tool: run `update-pins --pins src/Meziantou.Framework.Geo.Maps.Tool/data-versions.json`
  from the repository to update them.
- `Resources/Boundaries` holds the answers of the [lookup of Nominatim](https://nominatim.org/release-docs/latest/api/Lookup/)
  for the relations of `OsmBoundariesBackfill`, such as
  `https://nominatim.openstreetmap.org/lookup?osm_ids=R193084&format=geojson&polygon_geojson=1`
  (© OpenStreetMap contributors, ODbL 1.0). They are updated by hand.

<!-- help -->
## Help

```
Description:
  Generates offline map bundles for Meziantou.Framework.Geo.Maps, from OpenStreetMap and Natural Earth.

Usage:
  Meziantou.Framework.Geo.Maps.Tool [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  build        Builds the bundles of regions
  pack         Builds the bundles of regions, and a NuGet package per bundle: by default, an assembly built by the .NET SDK that embeds the map and opens it with Map.Region_<Key>
  list         Lists the regions: their identifier, their key (the name of their bundle), their ISO 3166-1 code and their continent
  update-pins  Downloads the latest versions of the sources, and pins them in a file to use with --pins
```

### build

```
Description:
  Builds the bundles of regions

Usage:
  Meziantou.Framework.Geo.Maps.Tool build [options]

Options:
  --region <region>      The regions to build, separated by commas. A region is world, a continent (africa, antarctica, asia, europe, north-america, oceania, south-america), or a country or territory: its English name in lowercase words joined by hyphens (switzerland, united-states, cote-divoire), its key (Switzerland, UnitedStates) or its ISO 3166-1 alpha-2 code (CH, US). The list command lists them.
  --all                  Builds every region: the world, the continents, and every country and territory
  --output <output>      The folder of the bundles [default: maps]
  --format <format>      The format of the bundles: fgb (a folder of FlatGeobuf files per region), geojson (a folder of GeoJSON files per region) or gpkg (a GeoPackage file per region) [default: fgb]
  --layers <layers>      The layers to write, separated by commas: borders, states-provinces, cities, rivers, lakes, coastline, land, ocean, marine-areas, physical-regions (default: all of them)
  --previews <previews>  A folder to render a preview of each bundle to, as SVG and PNG
  --pins <pins>          A file of pins to use instead of the pins of the tool, as the update-pins command writes it
  --cache <cache>        The folder of the downloaded sources (default: the application data folder of the user)
  -?, -h, --help         Show help and usage information
```

### list

```
Description:
  Lists the regions: their identifier, their key (the name of their bundle), their ISO 3166-1 code and their continent

Usage:
  Meziantou.Framework.Geo.Maps.Tool list [options]

Options:
  --pins <pins>    A file of pins to use instead of the pins of the tool, as the update-pins command writes it
  --cache <cache>  The folder of the downloaded sources (default: the application data folder of the user)
  -?, -h, --help   Show help and usage information
```

### pack

```
Description:
  Builds the bundles of regions, and a NuGet package per bundle: by default, an assembly built by the .NET SDK that embeds the map and opens it with Map.Region_<Key>

Usage:
  Meziantou.Framework.Geo.Maps.Tool pack [options]

Options:
  --region <region>                        The regions to build, separated by commas. A region is world, a continent (africa, antarctica, asia, europe, north-america, oceania, south-america), or a country or territory: its English name in lowercase words joined by hyphens (switzerland, united-states, cote-divoire), its key (Switzerland, UnitedStates) or its ISO 3166-1 alpha-2 code (CH, US). The list command lists them.
  --all                                    Builds every region: the world, the continents, and every country and territory
  --output <output>                        The folder of the bundles [default: maps]
  --format <format>                        The format of the bundles: fgb (a folder of FlatGeobuf files per region), geojson (a folder of GeoJSON files per region) or gpkg (a GeoPackage file per region) [default: fgb]
  --layers <layers>                        The layers to write, separated by commas: borders, states-provinces, cities, rivers, lakes, coastline, land, ocean, marine-areas, physical-regions (default: all of them)
  --previews <previews>                    A folder to render a preview of each bundle to, as SVG and PNG
  --pins <pins>                            A file of pins to use instead of the pins of the tool, as the update-pins command writes it
  --cache <cache>                          The folder of the downloaded sources (default: the application data folder of the user)
  --package-output <package-output>        The folder of the packages [default: packages]
  --package-id-prefix <package-id-prefix>  The prefix of the identifiers of the packages, followed by the key of the region, such as GeoMaps.France [default: GeoMaps]
  --package-version <package-version>      The version of the packages [default: 1.0.0]
  --authors <authors>                      The authors of the packages (default: the prefix of their identifiers)
  --package-content <package-content>      How the packages ship the maps: assembly (an assembly that embeds the map, built with the .NET SDK, opened with Map.Region_<Key>) or files (files copied to the maps folder next to the application, read by MapCatalog.Default; required for gpkg) [default: assembly]
  -?, -h, --help                           Show help and usage information
```

### update-pins

```
Description:
  Downloads the latest versions of the sources, and pins them in a file to use with --pins

Usage:
  Meziantou.Framework.Geo.Maps.Tool update-pins [options]

Options:
  --pins <pins> (REQUIRED)  The file of pins to write; its date is kept when the pins do not change
  --cache <cache>           The folder of the downloaded sources (default: the application data folder of the user)
  -?, -h, --help            Show help and usage information
```
<!-- help -->