using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Meziantou.Framework.Geo.Maps.GeoPackage;
using Meziantou.Framework.Geo.Maps.Tool;

namespace Meziantou.Framework.Geo.Maps.Tool.Tests;

public sealed class GeoMapsToolTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Build_All()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var output = temp.FullPath / "maps";
        var (exitCode, console) = await RunAsync("build", "--all", "--pins", pins, "--cache", cache, "--output", output, "--previews", temp.FullPath / "previews");
        Assert.Equal(0, exitCode);
        Assert.Contains("Built 4 regions (FlatGeobuf)", console.Output);
        Assert.Contains("  skip  Africa: no data", console.Output);

        // A folder of FlatGeobuf files per region, with the date of the pins
        var catalog = new MapCatalog(output);
        Assert.Equal(["Europe", "France", "Switzerland", "World"], catalog.Regions);
        var world = catalog.GetMap("World");
        Assert.HasCount(2, world.Load(MapLayer.Borders));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), world.Metadata.LastModified);
        Assert.StartsWith("© OpenStreetMap contributors", world.Attribution);

        // The sources cut to each region: Switzerland has no sea, and Null Island is not a physical region
        var switzerland = catalog.GetMap("Switzerland");
        Assert.Equal([MapLayer.Borders, MapLayer.StatesProvinces, MapLayer.Cities, MapLayer.Rivers, MapLayer.Lakes, MapLayer.PhysicalRegions], switzerland.Layers);
        Assert.Equal(new GeoBounds(6, 45, 10, 48), switzerland.Bounds);
        Assert.Equal(["CH-BE", "CH-ZH"], switzerland.Load(MapLayer.StatesProvinces).Select(state => (string)state.Properties["iso"]!).Order(StringComparer.Ordinal));
        var bern = Assert.Single(switzerland.Load(MapLayer.Cities));
        Assert.Equal("Bern", bern.Properties["name"]);
        Assert.Equal(true, bern.Properties["capital"]);
        var alps = Assert.Single(switzerland.Load(MapLayer.PhysicalRegions));
        Assert.Equal("range/mtn", alps.Properties["kind"]);
        Assert.Equal(new GeoBounds(6.5, 45, 10, 47.5), alps.Geometry!.GetBounds());

        var france = catalog.GetMap("France");
        Assert.Equal([MapLayer.Borders, MapLayer.StatesProvinces, MapLayer.Cities, MapLayer.Rivers, MapLayer.Coastline, MapLayer.Land, MapLayer.Ocean, MapLayer.MarineAreas, MapLayer.PhysicalRegions], france.Layers);
        Assert.Equal(["Lyon", "Paris"], france.Load(MapLayer.Cities).Select(city => (string)city.Properties["name"]!).Order(StringComparer.Ordinal));
        Assert.Equal(["Bay of Biscay"], france.Load(MapLayer.MarineAreas).Select(area => (string)area.Properties["name"]!));
        Assert.Equal(["Pointe du Raz"], france.Load(MapLayer.PhysicalRegions).Select(region => (string)region.Properties["name"]!));

        Assert.True(File.Exists(temp.FullPath / "previews" / "France.png"));
        Assert.True(File.Exists(temp.FullPath / "previews" / "World.svg"));

        // The same pins give the same files
        var again = temp.FullPath / "again";
        Assert.Equal(0, (await RunAsync("build", "--all", "--pins", pins, "--cache", cache, "--output", again)).ExitCode);
        foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(again / FullPath.FromPath(file).MakePathRelativeTo(output)));
        }
    }

    [Fact]
    public async Task Build_FormatAndLayers()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var output = temp.FullPath / "maps";
        var (exitCode, _) = await RunAsync("build", "--region", "ch,France", "--format", "gpkg", "--layers", "borders,StatesProvinces", "--pins", pins, "--cache", cache, "--output", output);
        Assert.Equal(0, exitCode);
        Assert.Equal(["France.gpkg", "Switzerland.gpkg"], Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        var map = new MapCatalog(output, [GeoPackageMapFormat.Instance]).GetMap("France");
        Assert.Equal([MapLayer.Borders, MapLayer.StatesProvinces], map.Layers);

        // The bounds come from the borders, even when the borders are not written
        Assert.Equal(0, (await RunAsync("build", "--region", "fr", "--layers", "cities", "--pins", pins, "--cache", cache, "--output", temp.FullPath / "cities")).ExitCode);
        var cities = new MapCatalog(temp.FullPath / "cities").GetMap("France");
        Assert.Equal([MapLayer.Cities], cities.Layers);
        Assert.Equal(new GeoBounds(-2, 43, 6, 49), cities.Bounds);
    }

    [Fact]
    public async Task Pack()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var packages = temp.FullPath / "packages";
        var (exitCode, console) = await RunAsync("pack", "--region", "switzerland", "--package-id-prefix", "Contoso.Maps.", "--package-version", "2.0.0", "--authors", "Contoso", "--package-output", packages, "--pins", pins, "--cache", cache, "--output", temp.FullPath / "maps");
        Assert.Equal(0, exitCode);
        Assert.Contains("Packed 1 packages", console.Output);

        // An assembly that embeds the map: no file to copy next to the application
        using var zip = ZipFile.OpenRead(packages / "Contoso.Maps.Switzerland.2.0.0.nupkg");
        var entries = zip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("lib/net10.0/Contoso.Maps.Switzerland.dll", entries);
        Assert.Contains("lib/net10.0/Contoso.Maps.Switzerland.xml", entries);
        Assert.DoesNotContain(entries, entry => entry.StartsWith("data/", StringComparison.Ordinal) || entry.StartsWith("buildTransitive/", StringComparison.Ordinal));
        using var reader = new StreamReader(zip.GetEntry("Contoso.Maps.Switzerland.nuspec")!.Open());
        var nuspec = await reader.ReadToEndAsync();
        Assert.Contains("<id>Contoso.Maps.Switzerland</id>", nuspec);
        Assert.Contains("<authors>Contoso</authors>", nuspec);
        Assert.Contains("ODbL-1.0", nuspec);
        Assert.Contains("<dependency id=\"Meziantou.Framework.Geo.Maps\"", nuspec);
        Assert.Contains("borders, states and provinces, cities, rivers, lakes and physical regions", nuspec);
        Assert.Contains("Map.Region_Switzerland", nuspec);
    }

    [Fact]
    public async Task Pack_ProjectOutput()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var packages = temp.FullPath / "packages";

        // The projects import the Directory.Build.props file above their folder, which gives the target frameworks
        var repository = temp.FullPath / "repository";
        Directory.CreateDirectory(repository);
        File.WriteAllText(repository / "Directory.Build.props", "<Project><PropertyGroup><MapTargetFrameworks>net11.0;net10.0</MapTargetFrameworks></PropertyGroup></Project>");
        var (exitCode, console) = await RunAsync("pack", "--region", "switzerland", "--package-id-prefix", "Contoso.Maps", "--package-target-framework", "$(MapTargetFrameworks)", "--package-sdk", "Microsoft.NET.Sdk", "--package-project-output", repository / "maps", "--package-output", packages, "--pins", pins, "--cache", cache, "--output", temp.FullPath / "maps");
        Assert.Equal(0, exitCode);
        Assert.Contains("Packed 1 packages", console.Output);
        Assert.True(File.Exists(repository / "maps" / "Contoso.Maps.Switzerland" / "Contoso.Maps.Switzerland.csproj"));
        Assert.False(File.Exists(repository / "maps" / "Contoso.Maps.Switzerland" / "Directory.Build.props"));

        using var zip = ZipFile.OpenRead(packages / "Contoso.Maps.Switzerland.1.0.0.nupkg");
        var entries = zip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("lib/net10.0/Contoso.Maps.Switzerland.dll", entries);
        Assert.Contains("lib/net11.0/Contoso.Maps.Switzerland.dll", entries);
        using var reader = new StreamReader(zip.GetEntry("Contoso.Maps.Switzerland.nuspec")!.Open());
        var nuspec = await reader.ReadToEndAsync();
        Assert.Contains("<group targetFramework=\"net10.0\">", nuspec);
        Assert.Contains("<group targetFramework=\"net11.0\">", nuspec);
    }

    [Fact]
    public async Task Pack_Files()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var packages = temp.FullPath / "packages";
        var (exitCode, _) = await RunAsync("pack", "--region", "switzerland", "--package-content", "files", "--package-id-prefix", "Contoso.Maps.", "--package-version", "2.0.0", "--authors", "Contoso", "--package-output", packages, "--pins", pins, "--cache", cache, "--output", temp.FullPath / "maps");
        Assert.Equal(0, exitCode);

        using var zip = ZipFile.OpenRead(packages / "Contoso.Maps.Switzerland.2.0.0.nupkg");
        Assert.Contains("data/Switzerland/borders.fgb", zip.Entries.Select(entry => entry.FullName));
        Assert.Contains("buildTransitive/Contoso.Maps.Switzerland.targets", zip.Entries.Select(entry => entry.FullName));
        using var reader = new StreamReader(zip.GetEntry("Contoso.Maps.Switzerland.nuspec")!.Open());
        var nuspec = await reader.ReadToEndAsync();
        Assert.Contains("<id>Contoso.Maps.Switzerland</id>", nuspec);
        Assert.Contains("<authors>Contoso</authors>", nuspec);
        Assert.Contains("<dependency id=\"Meziantou.Framework.Geo.Maps\"", nuspec);
        Assert.Contains("borders, states and provinces, cities, rivers, lakes and physical regions", nuspec);
    }

    [Fact]
    public async Task List()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var (exitCode, console) = await RunAsync("list", "--pins", pins, "--cache", cache);
        Assert.Equal(0, exitCode);
        var lines = console.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains(lines, line => line.StartsWith("world", StringComparison.Ordinal));
        Assert.Contains(lines, line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) is "switzerland Switzerland CH europe");
        Assert.Contains(lines, line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) is "france France FR europe");
    }

    [Theory]
    [InlineData("--format", "shp", "Unknown format: shp")]
    [InlineData("--layers", "borders,roads", "Unknown layer: roads")]
    [InlineData("--region", "atlantis", "Unknown regions: atlantis")]
    [InlineData("--all", null, "not both")]
    public async Task Build_InvalidArguments(string option, string? value, string message)
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        string[] args = ["build", "--region", "ch", option, .. value is null ? Array.Empty<string>() : [value], "--pins", pins, "--cache", cache, "--output", temp.FullPath / "maps"];
        var (exitCode, console) = await RunAsync(args);
        Assert.Equal(1, exitCode);
        Assert.Contains(message, console.Error);
        Assert.False(Directory.Exists(temp.FullPath / "maps" / "Switzerland"));
    }

    [Fact]
    public async Task InvalidArguments()
    {
        var (exitCode, console) = await RunAsync("build");
        Assert.Equal(1, exitCode);
        Assert.Contains("--region or --all", console.Error);

        (exitCode, console) = await RunAsync("pack", "--region", "ch", "--package-id-prefix", "Contoso Maps");
        Assert.Equal(1, exitCode);
        Assert.Contains("not a valid prefix", console.Error);

        (exitCode, console) = await RunAsync("pack", "--region", "ch", "--package-content", "sources");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown package content: sources", console.Error);

        (exitCode, console) = await RunAsync("pack", "--region", "ch", "--package-target-framework", " ");
        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be empty", console.Error);

        (exitCode, console) = await RunAsync("pack", "--region", "ch", "--format", "gpkg");
        Assert.Equal(1, exitCode);
        Assert.Contains("--package-content files", console.Error);
    }

    [Fact]
    public async Task Build_MissingPin()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        File.WriteAllText(pins, File.ReadAllText(pins).Replace(Sources.CountryLevelsSource.BordersUrl, "https://example.com/other.tgz", StringComparison.Ordinal));
        var (exitCode, console) = await RunAsync("build", "--region", "ch", "--pins", pins, "--cache", cache, "--output", temp.FullPath / "maps");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("is not pinned", console.Error);
    }

    [Fact]
    public void EmbeddedSources()
    {
        // The pins of the tool give a version of every source; the boundaries of the backfill are in the tool
        var downloader = Downloader.Create(Downloader.GetEmbeddedPins(), "the pins of the tool", FullPath.GetTempPath(), update: false, TextWriter.Null);
        Assert.NotNull(downloader.NaturalEarthCommit);
        Assert.NotNull(downloader.CldrVersion);
        Assert.NotNull(downloader.Date);
        var backfill = Sources.OsmBoundariesBackfill.Load();
        Assert.HasCount(7, backfill.GetSubdivisions("DZ"));
        Assert.Equal(["CD-EQ", "CD-MN"], backfill.GetSubdivisions("CD").Select(subdivision => (string)subdivision.Properties["iso"]!));
    }

    [Theory]
    [InlineData("Switzerland", "switzerland", "Switzerland")]
    [InlineData("Côte d’Ivoire", "cote-divoire", "CoteDIvoire")]
    [InlineData("St. Kitts & Nevis", "st-kitts-and-nevis", "StKittsAndNevis")]
    [InlineData("São Tomé & Príncipe", "sao-tome-and-principe", "SaoTomeAndPrincipe")]
    [InlineData("U.S. Virgin Islands", "us-virgin-islands", "USVirginIslands")]
    [InlineData("Åland Islands", "aland-islands", "AlandIslands")]
    public void RegionNames(string name, string id, string key)
    {
        Assert.Equal(id, Regions.GetId(name));
        Assert.Equal(key, Regions.GetKey(name));
    }

    [Fact]
    public void RegionNames_UnknownLetter()
    {
        Assert.Throws<InvalidOperationException>(() => Regions.GetId("Ōsaka"));
    }

    [Fact]
    public void Regions_CountryInNoContinent()
    {
        var tree = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["150"] = ["CH"] };
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { ["CH"] = "Switzerland", ["XX"] = "Nowhere" };
        var regions = Regions.Build(tree, names, ["CH"]);
        Assert.Equal("europe", Assert.Single(regions, region => region.Iso is ["CH"]).Parent);

        var exception = Assert.Throws<InvalidOperationException>(() => Regions.Build(tree, names, ["CH", "XX"]));
        Assert.Contains("XX", exception.Message);
    }

    [Fact]
    public async Task RepositoryProjects_Update()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var directory = temp.FullPath / "Meziantou.Framework.Geo.Maps.Data";
        var switzerland = directory / "Meziantou.Framework.Geo.Maps.Data.Switzerland";
        var switzerlandProject = switzerland / "Meziantou.Framework.Geo.Maps.Data.Switzerland.csproj";
        var franceProject = directory / "Meziantou.Framework.Geo.Maps.Data.France" / "Meziantou.Framework.Geo.Maps.Data.France.csproj";

        // A project per region that has data, with the first version, the date of the pins, and the hashes of its layers
        var result = await UpdateRepositoryProjectsAsync(pins, cache, directory);
        Assert.Equal(["Europe", "France", "Switzerland", "World"], result.Added);
        var project = XDocument.Load(switzerlandProject);
        Assert.Equal("1.0.0", project.Descendants("Version").Single().Value);
        Assert.Equal("2026-01-02", project.Descendants("MapDataDate").Single().Value);
        Assert.Equal("$(MinimumTargetFramework)", project.Descendants("TargetFrameworks").Single().Value);
        var layer = project.Descendants("MapLayer").Single(element => (string?)element.Attribute("Include") == "borders.fgb");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(directory / "data" / "Switzerland" / "borders.fgb"))), (string?)layer.Attribute("Sha256"));
        Assert.Equal(["Meziantou.Framework.Geo.Maps.Data.Switzerland.csproj", "SwitzerlandMap.g.cs"], Directory.EnumerateFiles(switzerland).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains("LastModified = new global::System.DateTimeOffset(" + new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero).UtcTicks.ToString(CultureInfo.InvariantCulture) + "L", File.ReadAllText(switzerland / "SwitzerlandMap.g.cs"));

        // Newer pins with the same data: the projects keep their version and the date of their data
        File.WriteAllText(pins, File.ReadAllText(pins).Replace("2026-01-02", "2026-02-03", StringComparison.Ordinal));
        var projectText = File.ReadAllText(switzerlandProject);
        var codeText = File.ReadAllText(switzerland / "SwitzerlandMap.g.cs");
        result = await UpdateRepositoryProjectsAsync(pins, cache, directory);
        Assert.Equal(["Europe", "France", "Switzerland", "World"], result.Unchanged);
        Assert.Equal(projectText, File.ReadAllText(switzerlandProject));
        Assert.Equal(codeText, File.ReadAllText(switzerland / "SwitzerlandMap.g.cs"));

        // A layer that changed: only its region gets a new version and the date of the pins
        File.WriteAllText(switzerlandProject, projectText.Replace((string)layer.Attribute("Sha256")!, new string('0', 64), StringComparison.Ordinal));
        result = await UpdateRepositoryProjectsAsync(pins, cache, directory);
        Assert.Equal(["Switzerland"], result.Updated);
        project = XDocument.Load(switzerlandProject);
        Assert.Equal("1.0.1", project.Descendants("Version").Single().Value);
        Assert.Equal("2026-02-03", project.Descendants("MapDataDate").Single().Value);
        Assert.Equal("1.0.0", XDocument.Load(franceProject).Descendants("Version").Single().Value);

        // The code changed with the same pins: a new version, and the date of the data does not change
        File.AppendAllText(switzerland / "SwitzerlandMap.g.cs", "// edited\n");
        File.WriteAllText(switzerland / "notes.txt", "");
        result = await UpdateRepositoryProjectsAsync(pins, cache, directory);
        Assert.Equal(["Switzerland"], result.Updated);
        project = XDocument.Load(switzerlandProject);
        Assert.Equal("1.0.2", project.Descendants("Version").Single().Value);
        Assert.Equal("2026-02-03", project.Descendants("MapDataDate").Single().Value);
        Assert.False(File.Exists(switzerland / "notes.txt"));

        // The project of a region that no longer exists is removed
        Directory.CreateDirectory(directory / "Meziantou.Framework.Geo.Maps.Data.Atlantis");
        result = await UpdateRepositoryProjectsAsync(pins, cache, directory);
        Assert.Equal(["Atlantis"], result.Removed);
        Assert.False(Directory.Exists(directory / "Meziantou.Framework.Geo.Maps.Data.Atlantis"));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.9", "1.0.10")]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("2.0", "2.0.1")]
    public void RepositoryProjects_GetNextVersion(string previous, string expected)
    {
        Assert.Equal(expected, RepositoryProjects.GetNextVersion(previous));
    }

    private static async Task<RepositoryProjectsResult> UpdateRepositoryProjectsAsync(FullPath pins, FullPath cache, FullPath directory)
    {
        var downloader = Downloader.Create(await File.ReadAllTextAsync(pins), pins.Value, cache, update: false, TextWriter.Null);
        var sources = await SourceSet.LoadAsync(downloader, TextWriter.Null, CancellationToken.None);
        return await RepositoryProjects.UpdateAsync(sources, downloader.Date!.Value, directory, TextWriter.Null, CancellationToken.None);
    }

    private async Task<(int ExitCode, ConsoleHelper Console)> RunAsync(params string[] args)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(args, console.ConfigureConsole);
        return (exitCode, console);
    }
}
