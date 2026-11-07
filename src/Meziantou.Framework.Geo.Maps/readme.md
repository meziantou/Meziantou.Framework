# Meziantou.Framework.Geo.Maps

Loads offline map bundles: the borders, states and provinces, cities, rivers, lakes, coastline, land, ocean, seas and
physical regions of a region (the world, a continent or a country), stored next to the application as folders of
FlatGeobuf or GeoJSON files, or as GeoPackage files with `Meziantou.Framework.Geo.Maps.GeoPackage`. The layers are read
as the features of `Meziantou.Framework.Geo`, and draw maps with `Meziantou.Framework.Charts`.

## Usage

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

The package has no map of its own: build the bundles of the regions you need with
the `Meziantou.Framework.Geo.Maps.Tool` .NET tool.

## Formats

`MapCatalog` reads the folders of FlatGeobuf files (`MapFormat.FlatGeobuf`, the default of the tool) and of
GeoJSON files (`MapFormat.GeoJson`). `MapFormat.Write` writes a map in a format, such as a GeoJSON copy for a web page:

<!-- ref:../../tests/Meziantou.Framework.Geo.Maps.Tests/MapCatalogTests.cs;region=GeoMaps_ConvertFormat;format=md-fenced-code-block;dedent=true -->
```csharp
// Write the map of a region in another format: a folder of GeoJSON files, which web libraries read
var output = Directory.CreateTempSubdirectory().FullName;
var map = MapCatalog.Default.GetMap("Switzerland");
MapFormat.GeoJson.Write(MapFormat.GeoJson.GetPath(output, map.Region), map);
Assert.True(File.Exists(Path.Combine(output, "Switzerland", "states-provinces.geojson")));

// The default catalog reads both folder formats
var copy = new MapCatalog(output).GetMap("Switzerland");
Assert.HasCount(26, copy.Load(MapLayer.StatesProvinces));
```
<!-- endref -->

## Data

The borders and subdivisions are derived from OpenStreetMap (© OpenStreetMap contributors, ODbL) through
country-levels; the cities, rivers, lakes, coastline, land, ocean, marine areas and physical regions come from Natural
Earth (public domain). Show `Map.Attribution` on the maps you publish.
