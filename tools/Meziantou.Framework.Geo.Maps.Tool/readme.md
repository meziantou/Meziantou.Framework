# Meziantou.Framework.Geo.Maps.Tool

The tool that generates the offline map bundles `Meziantou.Framework.Geo.Maps` reads: for the world, each continent
and each country or territory, the borders, subdivisions, cities, rivers, lakes, coastline, land, ocean, seas and
physical regions, as folders of FlatGeobuf or GeoJSON files, GeoPackage files, or NuGet packages of assemblies that
embed them. It writes the projects of the map packages published on nuget.org, `Meziantou.Framework.Geo.Maps.Data.<Key>`.
The tool itself is not published: run it from the root of the repository.

```bash
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- build --region switzerland,france --previews previews
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- pack --region europe --package-id-prefix Contoso.Maps --package-version 1.0.0
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- list
dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- --help
```

`build` writes the bundles to `./maps`, where `MapCatalog` reads them when they are next to the application. `pack` also
writes a NuGet package per region to `./packages`, named after the prefix and the key of the region
(`Contoso.Maps.Europe`), for your own feed: an assembly, built with the .NET SDK, that embeds the map and opens it with
`Map.Region_Europe` (a C# 14 extension member, in the `Contoso.Maps` namespace). `--package-target-framework`,
`--package-sdk` and `--package-project-output` build the assemblies as the projects of your repository, such as with
`$(LatestTargetFrameworks)` and `Meziantou.NET.Sdk`. `--package-content files` makes packages that copy the map to the
`maps` folder next to the application instead, as GeoPackage bundles need. `list` lists the regions: the world, the continents, and every country and
territory, by their English name in lowercase words joined by hyphens, their key or their ISO code.

The first run downloads the sources, about 160 MB, to the application data folder of the user (`--cache` to choose
another folder), and loads them in about a minute, even for a single region. The next runs read them from the cache.

## Sources

The sources are pinned by SHA-256 in the tool: a commit of the repository always builds the same bundles, byte for byte.
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

- `data-versions.json` holds the pins of the tool: `update-pins --pins tools/Meziantou.Framework.Geo.Maps.Tool/data-versions.json`
  updates them.
- `src/Meziantou.Framework.Geo.Maps.Data` holds the projects of the map packages published on nuget.org,
  `Meziantou.Framework.Geo.Maps.Data.<Key>`: a project per region, written by the `update-projects` command (its code
  is in `RepositoryProjects.cs`), with its version, the SHA-256 of its layers and the code that opens
  its map (`Map.Region_<Key>`). The layers are not committed: `update-projects` writes them to the `data` folder, where the
  projects embed them from. A project compiles without them, but packing it fails unless every layer has its hash.
  `UseMapPlaceholderData=true` packs the projects without the data, with an empty file for each missing layer, as the
  `documentation` workflow does: such a package has no map, and must not be published.
  - A project gets a new version only when it changes: its layers, or the code `update-projects` writes. A new project
    has the version `1.0.0`, and the version is incremented on each change (`1.0.1`), like the other packages of the
    repository. The date of the data (`MapDataDate`) is the date of the pins when the layers change; a map whose layers
    do not change keeps the date of its data.
  - The `update-geo-maps-data` workflow runs `update-pins` and `update-projects` every week, and when the code of the tool
    changes. It opens a pull request with the projects that changed, their `ref/` files and the solution files (a draft
    when a project fails to build or pack, or the tests fail). Merging it publishes the new versions: the
    `create_nuget` job of the `publish` workflow builds the layers (`.github/actions/geo-maps-data`) when it packs a map
    project.
  - The map projects reference Meziantou.Framework.Geo.Maps: a change of it or of Meziantou.Framework.Geo packs them
    again. When it changes their layers, the pack fails until the projects are generated again in the same change,
    with `dotnet run --project tools/Meziantou.Framework.Geo.Maps.Tool -- update-projects`. A change of this tool packs
    nothing: the workflow generates the projects again after it is merged.
- `Resources/Boundaries` holds the answers of the [lookup of Nominatim](https://nominatim.org/release-docs/latest/api/Lookup/)
  for the relations of `OsmBoundariesBackfill`, such as
  `https://nominatim.openstreetmap.org/lookup?osm_ids=R193084&format=geojson&polygon_geojson=1`
  (© OpenStreetMap contributors, ODbL 1.0). They are updated by hand.
