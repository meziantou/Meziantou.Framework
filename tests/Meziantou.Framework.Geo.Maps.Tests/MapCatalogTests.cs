using Meziantou.Framework.Geo;
using Meziantou.Framework.Geo.Maps;

namespace Meziantou.Framework.Geo.Maps.Tests;

public sealed class MapCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geo-maps-" + Guid.NewGuid().ToString("N"));

    public static TheoryData<string> FolderFormats => ["FlatGeobuf", "GeoJSON"];

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Catalog()
    {
        CreateRegion("Beta", MapFormat.GeoJson, name: null);
        CreateRegion("Alpha", MapFormat.FlatGeobuf, "Alpha Land");
        Directory.CreateDirectory(Path.Combine(_directory, "Empty"));
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "");

        var catalog = new MapCatalog(_directory);
        Assert.Equal(Path.GetFullPath(_directory), catalog.Directory);
        Assert.Equal([MapFormat.FlatGeobuf, MapFormat.GeoJson], catalog.Formats);

        // A folder without layer is not a map
        Assert.Equal(["Alpha", "Beta"], catalog.Regions);
        Assert.True(catalog.Contains("Alpha"));
        Assert.True(catalog.Contains("Beta"));
        Assert.False(catalog.Contains("Empty"));
        Assert.False(catalog.Contains("Gamma"));
        Assert.False(catalog.TryGetMap("Gamma", out var missing));
        Assert.Null(missing);
        Assert.Throws<KeyNotFoundException>(() => catalog.GetMap("Gamma"));
        Assert.Empty(new MapCatalog(Path.Combine(_directory, "none")).Regions);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "maps"), MapCatalog.Default.Directory);

        // A region is the name of a file or a folder of the catalog, not a path
        Assert.Throws<ArgumentException>("region", () => catalog.GetMap(".."));
        Assert.Throws<ArgumentException>("region", () => catalog.GetMap("Alpha/../Beta"));
        Assert.Throws<ArgumentException>("region", () => catalog.GetMap("A*"));
        Assert.Throws<ArgumentException>("region", () => catalog.Contains(""));
        Assert.Throws<ArgumentException>("formats", () => new MapCatalog(_directory, []));
    }

    [Fact]
    public void Catalog_Formats()
    {
        CreateRegion("Alpha", MapFormat.GeoJson, "Alpha from GeoJSON");

        // A catalog reads only its formats
        var flatGeobuf = new MapCatalog(_directory, [MapFormat.FlatGeobuf]);
        Assert.Empty(flatGeobuf.Regions);
        Assert.False(flatGeobuf.Contains("Alpha"));
        Assert.Equal(["Alpha"], new MapCatalog(_directory, [MapFormat.GeoJson]).Regions);

        // The folder formats share the folder of a region: writing one replaces the other
        CreateRegion("Alpha", MapFormat.FlatGeobuf, "Alpha from FlatGeobuf");
        Assert.Equal(["borders.fgb", "cities.fgb", "metadata.json"], Directory.GetFiles(Path.Combine(_directory, "Alpha")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("Alpha from FlatGeobuf", new MapCatalog(_directory).GetMap("Alpha").Name);
        Assert.False(new MapCatalog(_directory, [MapFormat.GeoJson]).Contains("Alpha"));

        // A map in a format the catalog does not read is named in the error
        File.WriteAllBytes(Path.Combine(_directory, "Beta.gpkg"), []);
        var exception = Assert.Throws<KeyNotFoundException>(() => new MapCatalog(_directory).GetMap("Beta"));
        Assert.Contains("'Beta.gpkg'", exception.Message);
        Assert.Contains("GeoPackageMapFormat", exception.Message);
    }

    [Fact]
    public void Map_Metadata()
    {
        var metadata = new MapMetadata("Alpha")
        {
            Name = "Alpha Land",
            Attribution = "© Someone",
            Bounds = new GeoBounds(0, 0, 10, 10),
            LastModified = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero),
            Sources = [new MapSource("Someone", new Uri("https://example.com/"), "CC0-1.0"), new MapSource("Anonymous")],
        };
        MapFormat.FlatGeobuf.Write(Path.Combine(_directory, "Alpha"), metadata, CreateLayers("Alpha"));
        var map = new MapCatalog(_directory).GetMap("Alpha");
        Assert.Equal("Alpha", map.Region);
        Assert.Equal("Alpha Land", map.Name);
        Assert.Equal("© Someone", map.Attribution);
        Assert.Equal(new GeoBounds(0, 0, 10, 10), map.Bounds);
        Assert.Equal(metadata.LastModified, map.Metadata.LastModified);
        Assert.Equal(metadata.Sources, map.Metadata.Sources);

        // Without metadata, the name is the folder and the bounds are those of the borders
        CreateRegion("Beta", MapFormat.FlatGeobuf, name: null);
        File.Delete(Path.Combine(_directory, "Beta", "metadata.json"));
        var beta = new MapCatalog(_directory).GetMap("Beta");
        Assert.Equal("Beta", beta.Name);
        Assert.Null(beta.Attribution);
        Assert.Null(beta.Metadata.LastModified);
        Assert.Empty(beta.Metadata.Sources);
        Assert.Equal(new GeoBounds(0, 0, 4, 4), beta.Bounds);

        CreateRegion("Invalid", MapFormat.FlatGeobuf, name: null);
        File.WriteAllText(Path.Combine(_directory, "Invalid", "metadata.json"), "{");
        Assert.Throws<FormatException>(() => new MapCatalog(_directory).GetMap("Invalid"));
        File.WriteAllText(Path.Combine(_directory, "Invalid", "metadata.json"), "[]");
        Assert.Throws<FormatException>(() => new MapCatalog(_directory).GetMap("Invalid"));
    }

    [Theory]
    [MemberData(nameof(FolderFormats))]
    public void Map_Layers(string formatName)
    {
        var format = GetFormat(formatName);
        CreateRegion("Alpha", format, name: null);
        var map = new MapCatalog(_directory).GetMap("Alpha");
        Assert.Equal([MapLayer.Borders, MapLayer.Cities], map.Layers);
        Assert.True(map.HasLayer(MapLayer.Cities));
        Assert.False(map.HasLayer(MapLayer.Ocean));

        var cities = map.Load(MapLayer.Cities);
        Assert.Equal("Cities", cities.Name);
        Assert.HasCount(2, cities);
        var north = Assert.Single(map.Load(MapLayer.Cities, new GeoBounds(0, 2, 4, 4)));
        Assert.Equal("North", north.Properties["name"]);
        Assert.Empty(map.Load(MapLayer.Cities, new GeoBounds(10, 10, 20, 20)));
        Assert.Throws<KeyNotFoundException>(() => map.Load(MapLayer.Ocean));
        Assert.Throws<ArgumentOutOfRangeException>("layer", () => map.Load((MapLayer)100));

        // Writing a map again removes the layers it no longer has
        format.Write(format.GetPath(_directory, "Alpha"), map.Metadata, new Dictionary<MapLayer, GeoFeatureCollection> { [MapLayer.Borders] = map.Load(MapLayer.Borders) });
        Assert.Equal([MapLayer.Borders], new MapCatalog(_directory).GetMap("Alpha").Layers);
    }

    [Theory]
    [MemberData(nameof(FolderFormats))]
    public void Map_RoundTrip(string formatName)
    {
        var format = GetFormat(formatName);
        var source = MapCatalog.Default.GetMap("Switzerland");
        format.Write(format.GetPath(_directory, source.Region), source);

        var map = new MapCatalog(_directory, [format]).GetMap("Switzerland");
        Assert.Equal(source.Name, map.Name);
        Assert.Equal(source.Attribution, map.Attribution);
        Assert.Equal(source.Bounds, map.Bounds);
        Assert.Equal(source.Metadata.Sources, map.Metadata.Sources);
        Assert.Equal(source.Layers, map.Layers);
        foreach (var layer in source.Layers)
        {
            var expected = source.Load(layer);
            var actual = map.Load(layer);
            Assert.HasCount(expected.Count, actual);
            Assert.Equal(expected.GetBounds(), actual.GetBounds());
        }

        // The documented properties get their types in every format: GeoJSON reads the numbers as doubles
        var bern = map.Load(MapLayer.Cities).Single(city => city.Properties["name"] is "Bern");
        Assert.Equal(true, bern.Properties["capital"]);
        Assert.IsType<int>(bern.Properties["rank"]);
        Assert.IsType<long>(bern.Properties["population"]);
        Assert.IsType<int>(map.Load(MapLayer.StatesProvinces)[0].Properties["admin_level"]);
        Assert.All(map.Load(MapLayer.PhysicalRegions), region => Assert.IsType<int>(region.Properties["scale_rank"]));
    }

    [Fact]
    public void LoadMap()
    {
        #region GeoMaps_LoadMap
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
        #endregion

        Assert.Equal([MapLayer.Borders, MapLayer.StatesProvinces, MapLayer.Cities, MapLayer.Rivers, MapLayer.Lakes, MapLayer.PhysicalRegions], map.Layers);

        // The physical regions of Natural Earth, cut to the bounds of Switzerland
        var alps = map.Load(MapLayer.PhysicalRegions).Single(region => region.Properties["name"] is "ALPS");
        Assert.Equal("range/mtn", alps.Properties["kind"]);
        Assert.Equal(1, alps.Properties["scale_rank"]);
        var alpsBounds = alps.Geometry!.GetBounds()!.Value;
        Assert.True(map.Bounds!.Value.Contains(new GeoPosition(alpsBounds.West, alpsBounds.South)));
        Assert.True(map.Bounds.Value.Contains(new GeoPosition(alpsBounds.East, alpsBounds.North)));
        Assert.Contains("Switzerland", MapCatalog.Default.Regions);
    }

    [Fact]
    public void LoadBounds()
    {
        #region GeoMaps_LoadBounds
        var map = MapCatalog.Default.GetMap("Switzerland");

        // The cities around Lake Geneva: only the features in the box are decoded
        var lakeGeneva = new GeoBounds(West: 5.9, South: 46.1, East: 7.0, North: 46.6);
        var cities = map.Load(MapLayer.Cities, lakeGeneva);
        Assert.Equal(["Geneva", "Lausanne"], cities.Select(city => (string)city.Properties["name"]!).Order(StringComparer.Ordinal));
        #endregion

        Assert.True(map.Bounds!.Value.Contains(new GeoPosition(7.45, 46.95)));
    }

    [Fact]
    public void ConvertFormat()
    {
        #region GeoMaps_ConvertFormat
        // Write the map of a region in another format: a folder of GeoJSON files, which web libraries read
        var output = Directory.CreateTempSubdirectory().FullName;
        var map = MapCatalog.Default.GetMap("Switzerland");
        MapFormat.GeoJson.Write(MapFormat.GeoJson.GetPath(output, map.Region), map);
        Assert.True(File.Exists(Path.Combine(output, "Switzerland", "states-provinces.geojson")));

        // The default catalog reads both folder formats
        var copy = new MapCatalog(output).GetMap("Switzerland");
        Assert.HasCount(26, copy.Load(MapLayer.StatesProvinces));
        #endregion

        Directory.Delete(output, recursive: true);
    }

    [Fact]
    public void GetLayerName()
    {
        Assert.Equal("borders", MapFormat.GetLayerName(MapLayer.Borders));
        Assert.Equal("states-provinces", MapFormat.GetLayerName(MapLayer.StatesProvinces));
        Assert.HasCount(Enum.GetValues<MapLayer>().Length, Enum.GetValues<MapLayer>().Select(MapFormat.GetLayerName).Distinct(StringComparer.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>("layer", () => MapFormat.GetLayerName((MapLayer)100));
        Assert.Equal("FlatGeobuf", MapFormat.FlatGeobuf.Name);
        Assert.Equal("GeoJSON", MapFormat.GeoJson.Name);
    }

    private static MapFormat GetFormat(string name) => name is "FlatGeobuf" ? MapFormat.FlatGeobuf : MapFormat.GeoJson;

    private void CreateRegion(string region, MapFormat format, string? name)
    {
        format.Write(format.GetPath(_directory, region), new MapMetadata(region) { Name = name }, CreateLayers(region));
    }

    private static Dictionary<MapLayer, GeoFeatureCollection> CreateLayers(string region) => new()
    {
        [MapLayer.Borders] =
        [
            new GeoFeature(new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)]]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = region }),
        ],
        [MapLayer.Cities] =
        [
            new GeoFeature(new GeoPoint(new(1, 1)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "South" }),
            new GeoFeature(new GeoPoint(new(1, 3)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "North" }),
        ],
    };
}
