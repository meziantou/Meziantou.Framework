using System.Text.Json;
using Meziantou.Framework.Geo;
using Meziantou.Framework.Geo.Maps;
using Meziantou.Framework.Geo.Maps.GeoPackage;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Tests;

public sealed class GeoPackageMapFormatTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("geo-maps-gpkg-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void RoundTrip()
    {
        var format = GeoPackageMapFormat.Instance;
        var source = MapCatalog.Default.GetMap("Switzerland");
        var path = format.GetPath(_directory, source.Region);
        Assert.Equal(Path.Combine(_directory, "Switzerland.gpkg"), path);
        format.Write(path, source);

        Assert.True(format.TryGetRegion(path, out var region));
        Assert.Equal("Switzerland", region);
        var map = format.Open(path);
        Assert.Equal(source.Name, map.Name);
        Assert.Equal(source.Attribution, map.Attribution);
        Assert.Equal(source.Bounds, map.Bounds);
        Assert.Equal(source.Metadata.LastModified, map.Metadata.LastModified);
        Assert.Equal(source.Metadata.Sources, map.Metadata.Sources);
        Assert.Equal(source.Layers, map.Layers);
        foreach (var layer in source.Layers)
        {
            // FlatGeobuf orders the features along its index: the GeoPackage keeps the order it was given
            var expected = source.Load(layer);
            var actual = map.Load(layer);
            Assert.Equal(layer.ToString(), actual.Name);
            Assert.HasCount(expected.Count, actual);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Geometry!.GetPositions(), actual[i].Geometry!.GetPositions());
                Assert.Equal(expected[i].Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal), actual[i].Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
            }
        }

        var bern = map.Load(MapLayer.Cities).Single(city => city.Properties["name"] is "Bern");
        Assert.Equal(true, bern.Properties["capital"]);
        Assert.IsType<int>(bern.Properties["rank"]);
        Assert.IsType<long>(bern.Properties["population"]);
    }

    [Fact]
    public void LoadBounds()
    {
        var path = WriteSwitzerland();
        var map = GeoPackageMapFormat.Instance.Open(path);

        // The R-tree finds the candidates; the map keeps the features whose bounds intersect the box
        var cities = map.Load(MapLayer.Cities, new GeoBounds(West: 5.9, South: 46.1, East: 7.0, North: 46.6));
        Assert.Equal(["Geneva", "Lausanne"], cities.Select(city => (string)city.Properties["name"]!).Order(StringComparer.Ordinal));
        Assert.Empty(map.Load(MapLayer.Cities, new GeoBounds(0, 0, 1, 1)));
        Assert.HasCount(26, map.Load(MapLayer.StatesProvinces, map.Bounds!.Value));
    }

    [Fact]
    public void Write_IsDeterministic()
    {
        var map = MapCatalog.Default.GetMap("Switzerland");
        var first = Path.Combine(_directory, "first", "Switzerland.gpkg");
        var second = Path.Combine(_directory, "second", "Switzerland.gpkg");
        GeoPackageMapFormat.Instance.Write(first, map);
        GeoPackageMapFormat.Instance.Write(second, map);
        Assert.NotNull(map.Metadata.LastModified);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));

        // Writing again replaces the file
        GeoPackageMapFormat.Instance.Write(first, map.Metadata, new Dictionary<MapLayer, GeoFeatureCollection> { [MapLayer.Borders] = map.Load(MapLayer.Borders) });
        Assert.Equal([MapLayer.Borders], GeoPackageMapFormat.Instance.Open(first).Layers);
    }

    [Fact]
    public void Properties()
    {
        using var json = JsonDocument.Parse("""{"a":[1,2]}""");
        var date = new DateTimeOffset(2026, 11, 6, 12, 30, 15, 250, TimeSpan.FromHours(2));
        var features = new GeoFeatureCollection
        {
            new GeoFeature(new GeoPoint(new(1, 1)), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["text"] = "a",
                ["boolean"] = true,
                ["int"] = 1,
                ["long"] = 5_000_000_000L,
                ["double"] = 1.5,
                ["date"] = date,
                ["bytes"] = new byte[] { 1, 2, 3 },
                ["json"] = json.RootElement.GetProperty("a"),
                ["number"] = 1,
                ["mixed"] = 1,
                ["absent"] = null,
            }),
            new GeoFeature(new GeoPoint(new(2, 2)), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["number"] = 2.5,
                ["mixed"] = "b",
            }),
        };
        var map = WriteAndOpen(new() { [MapLayer.Cities] = features });
        var cities = map.Load(MapLayer.Cities);
        var first = cities[0].Properties;
        Assert.Equal("a", first["text"]);
        Assert.Equal(true, first["boolean"]);
        Assert.Equal(1, first["int"]);
        Assert.Equal(5_000_000_000L, first["long"]);
        Assert.Equal(1.5, first["double"]);
        Assert.Equal(date, first["date"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])first["bytes"]!);
        Assert.Equal("[1,2]", first["json"]);

        // A column of mixed values gets the type that holds them all: a double for numbers, else the text
        Assert.Equal(1.0, first["number"]);
        Assert.Equal(2.5, cities[1].Properties["number"]);
        Assert.Equal("1", first["mixed"]);
        Assert.Equal("b", cities[1].Properties["mixed"]);

        // A null value, or a property a feature does not have, is not there
        Assert.False(first.ContainsKey("absent"));
        Assert.False(cities[1].Properties.ContainsKey("text"));
    }

    [Fact]
    public void Geometries()
    {
        var geometries = new GeoGeometry?[]
        {
            new GeoPoint(new(1, 2)),
            new GeoPoint(new(1, 2, 300)),
            new GeoLineString([new(0, 0), new(1, 1)]),
            new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 0)]]),
            new GeoMultiPoint([new(0, 0), new(1, 1)]),
            new GeoMultiLineString([new GeoLineString([new(0, 0), new(1, 1)]), new GeoLineString([new(2, 2), new(3, 3)])]),
            new GeoMultiPolygon([new GeoPolygon([[new(0, 0), new(1, 0), new(1, 1), new(0, 0)]])]),
            new GeoGeometryCollection([new GeoPoint(new(5, 5)), new GeoLineString([new(0, 0), new(1, 1)])]),
            null,
        };
        var map = WriteAndOpen(new() { [MapLayer.Rivers] = [.. geometries.Select(geometry => new GeoFeature(geometry))] });
        var rivers = map.Load(MapLayer.Rivers);
        Assert.HasCount(geometries.Length, rivers);
        for (var i = 0; i < geometries.Length; i++)
        {
            Assert.Equal(geometries[i]?.Type, rivers[i].Geometry?.Type);
            Assert.Equal(geometries[i]?.GetPositions(), rivers[i].Geometry?.GetPositions());
        }

        // A feature without geometry is not in the R-tree, so a box never finds it
        Assert.HasCount(geometries.Length - 1, map.Load(MapLayer.Rivers, new GeoBounds(-10, -10, 10, 10)));
    }

    [Fact]
    public void Write_InvalidColumnNames()
    {
        foreach (var name in new[] { "fid", "GEOM" })
        {
            var features = new GeoFeatureCollection { new GeoFeature(new GeoPoint(new(1, 1)), new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = 1 }) };
            Assert.Throws<ArgumentException>("features", () => WriteAndOpen(new() { [MapLayer.Cities] = features }));
        }

        // SQLite compares the names of the columns without case
        var duplicate = new GeoFeatureCollection { new GeoFeature(new GeoPoint(new(1, 1)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "a", ["Name"] = "b" }) };
        Assert.Throws<ArgumentException>("features", () => WriteAndOpen(new() { [MapLayer.Cities] = duplicate }));
    }

    [Fact]
    public void Open_InvalidFile()
    {
        var path = Path.Combine(_directory, "Invalid.gpkg");
        File.WriteAllText(path, "This is not a GeoPackage file, nor a SQLite database.");
        Assert.True(GeoPackageMapFormat.Instance.TryGetRegion(path, out _));
        Assert.Throws<FormatException>(() => GeoPackageMapFormat.Instance.Open(path));

        Assert.False(GeoPackageMapFormat.Instance.TryGetRegion(Path.Combine(_directory, "Missing.gpkg"), out _));
        Assert.False(GeoPackageMapFormat.Instance.TryGetRegion(_directory, out _));
        Assert.Equal("GeoPackage", GeoPackageMapFormat.Instance.Name);
    }

    [Fact]
    public void Catalog()
    {
        #region GeoMapsGeoPackage_Catalog
        // Write the map of a region as a GeoPackage file, which QGIS and GDAL open, such as maps/Switzerland.gpkg
        var directory = Directory.CreateTempSubdirectory().FullName;
        var switzerland = MapCatalog.Default.GetMap("Switzerland");
        GeoPackageMapFormat.Instance.Write(GeoPackageMapFormat.Instance.GetPath(directory, switzerland.Region), switzerland);

        // A catalog reads the GeoPackage files when it has the format, here before the folder formats
        var catalog = new MapCatalog(directory, [GeoPackageMapFormat.Instance, .. MapCatalog.DefaultFormats]);
        Assert.Equal(["Switzerland"], catalog.Regions);

        var map = catalog.GetMap("Switzerland");
        Assert.HasCount(26, map.Load(MapLayer.StatesProvinces));
        #endregion

        // With a map of a region in two formats, the first format of the catalog wins
        MapFormat.FlatGeobuf.Write(MapFormat.FlatGeobuf.GetPath(directory, "Switzerland"), new MapMetadata("Switzerland") { Name = "Folder" }, new Dictionary<MapLayer, GeoFeatureCollection> { [MapLayer.Borders] = map.Load(MapLayer.Borders) });
        Assert.Equal("Switzerland", catalog.GetMap("Switzerland").Name);
        Assert.Equal("Folder", new MapCatalog(directory).GetMap("Switzerland").Name);
        Assert.Equal("Folder", new MapCatalog(directory, [.. MapCatalog.DefaultFormats, GeoPackageMapFormat.Instance]).GetMap("Switzerland").Name);
        Assert.Equal(["Switzerland"], catalog.Regions);
        Directory.Delete(directory, recursive: true);
    }

    private string WriteSwitzerland()
    {
        var path = GeoPackageMapFormat.Instance.GetPath(_directory, "Switzerland");
        GeoPackageMapFormat.Instance.Write(path, MapCatalog.Default.GetMap("Switzerland"));
        return path;
    }

    private Map WriteAndOpen(Dictionary<MapLayer, GeoFeatureCollection> layers)
    {
        var path = GeoPackageMapFormat.Instance.GetPath(_directory, "Test");
        GeoPackageMapFormat.Instance.Write(path, new MapMetadata("Test") { LastModified = DateTimeOffset.UnixEpoch }, layers);
        return GeoPackageMapFormat.Instance.Open(path);
    }
}
