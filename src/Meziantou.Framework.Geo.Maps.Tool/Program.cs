using System.CommandLine;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.Geo.Maps.GeoPackage;
using Meziantou.Framework.Geo.Maps.Tool.Sources;

[assembly: InternalsVisibleTo("Meziantou.Framework.Geo.Maps.Tool.Tests")]

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>
/// Generates map bundles: the layers of the world, of each continent and of each country, from OpenStreetMap
/// (country-levels) and Natural Earth, grouped into continents by Unicode CLDR, at the versions of the pins.
/// </summary>
internal static partial class Program
{
    private const string RegionHelp = "A region is world, a continent (africa, antarctica, asia, europe, north-america, oceania, south-america), or a country or territory: its English name in lowercase words joined by hyphens (switzerland, united-states, cote-divoire), its key (Switzerland, UnitedStates) or its ISO 3166-1 alpha-2 code (CH, US). The list command lists them.";

    public static Task<int> Main(string[] args) => MainImpl(args, configure: null);

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        var rootCommand = new RootCommand("Generates offline map bundles for Meziantou.Framework.Geo.Maps, from OpenStreetMap and Natural Earth.");
        rootCommand.Subcommands.Add(CreateBuildCommand(pack: false));
        rootCommand.Subcommands.Add(CreateBuildCommand(pack: true));
        rootCommand.Subcommands.Add(CreateListCommand());
        rootCommand.Subcommands.Add(CreateUpdatePinsCommand());

        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static Option<string?> CreatePinsOption() => new("--pins") { Description = "A file of pins to use instead of the pins of the tool, as the update-pins command writes it" };

    private static Option<string?> CreateCacheOption() => new("--cache") { Description = "The folder of the downloaded sources (default: the application data folder of the user)" };

    private static Command CreateBuildCommand(bool pack)
    {
        var regionOption = new Option<string[]>("--region") { Description = "The regions to build, separated by commas. " + RegionHelp, AllowMultipleArgumentsPerToken = true };
        var allOption = new Option<bool>("--all") { Description = "Builds every region: the world, the continents, and every country and territory" };
        var outputOption = new Option<string>("--output") { Description = "The folder of the bundles", DefaultValueFactory = _ => "maps" };
        var formatOption = new Option<string>("--format") { Description = "The format of the bundles: fgb (a folder of FlatGeobuf files per region), geojson (a folder of GeoJSON files per region) or gpkg (a GeoPackage file per region)", DefaultValueFactory = _ => "fgb" };
        var layersOption = new Option<string?>("--layers") { Description = "The layers to write, separated by commas: " + string.Join(", ", Enum.GetValues<MapLayer>().Select(MapFormat.GetLayerName)) + " (default: all of them)" };
        var previewsOption = new Option<string?>("--previews") { Description = "A folder to render a preview of each bundle to, as SVG and PNG" };
        var pinsOption = CreatePinsOption();
        var cacheOption = CreateCacheOption();

        var command = pack
            ? new Command("pack", "Builds the bundles of regions, and a NuGet package per bundle: by default, an assembly built by the .NET SDK that embeds the map and opens it with Map.Region_<Key>")
            : new Command("build", "Builds the bundles of regions");
        command.Options.Add(regionOption);
        command.Options.Add(allOption);
        command.Options.Add(outputOption);
        command.Options.Add(formatOption);
        command.Options.Add(layersOption);
        command.Options.Add(previewsOption);
        command.Options.Add(pinsOption);
        command.Options.Add(cacheOption);

        var packageOutputOption = new Option<string>("--package-output") { Description = "The folder of the packages", DefaultValueFactory = _ => "packages" };
        var packageIdPrefixOption = new Option<string>("--package-id-prefix") { Description = "The prefix of the identifiers of the packages, followed by the key of the region, such as GeoMaps.France", DefaultValueFactory = _ => "GeoMaps" };
        var packageVersionOption = new Option<string>("--package-version") { Description = "The version of the packages", DefaultValueFactory = _ => "1.0.0" };
        var authorsOption = new Option<string?>("--authors") { Description = "The authors of the packages (default: the prefix of their identifiers)" };
        var packageSdkOption = new Option<string>("--package-sdk") { Description = "The MSBuild SDK of the projects of the assemblies, such as Meziantou.NET.Sdk; the folders above the projects give its version, in a global.json file", DefaultValueFactory = _ => MapProjectWriter.DefaultSdk };
        var packageTargetFrameworkOption = new Option<string>("--package-target-framework") { Description = "The target frameworks of the assemblies, separated by semicolons (net10.0 or later), or an MSBuild property that gives them, such as $(LatestTargetFrameworks), defined in a Directory.Build.props file above --package-project-output", DefaultValueFactory = _ => MapProjectWriter.DefaultTargetFramework };
        var packageProjectOutputOption = new Option<string?>("--package-project-output") { Description = "A folder to write the projects of the assemblies to and keep them: the projects import the MSBuild files of the folders above them, such as Directory.Build.props (default: a temporary folder, where they import none)" };
        var packageContentOption = new Option<string>("--package-content") { Description = "How the packages ship the maps: assembly (an assembly that embeds the map, built with the .NET SDK, opened with Map.Region_<Key>) or files (files copied to the maps folder next to the application, read by MapCatalog.Default; required for gpkg)", DefaultValueFactory = _ => "assembly" };
        if (pack)
        {
            command.Options.Add(packageOutputOption);
            command.Options.Add(packageIdPrefixOption);
            command.Options.Add(packageVersionOption);
            command.Options.Add(authorsOption);
            command.Options.Add(packageContentOption);
            command.Options.Add(packageSdkOption);
            command.Options.Add(packageTargetFrameworkOption);
            command.Options.Add(packageProjectOutputOption);
        }

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            var error = parseResult.InvocationConfiguration.Error;
            var regionIds = (parseResult.GetValue(regionOption) ?? []).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
            var all = parseResult.GetValue(allOption);
            if (all == regionIds.Count > 0)
            {
                await error.WriteLineAsync("Choose the regions to build with --region or --all, not both.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            if (GetFormat(parseResult.GetRequiredValue(formatOption)) is not { } format)
            {
                await error.WriteLineAsync($"Unknown format: {parseResult.GetValue(formatOption)}. The formats are fgb, geojson and gpkg.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            if (GetLayers(parseResult.GetValue(layersOption), out var unknownLayer) is not { } layers)
            {
                await error.WriteLineAsync($"Unknown layer: {unknownLayer}. The layers are {string.Join(", ", Enum.GetValues<MapLayer>().Select(MapFormat.GetLayerName))}.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            PackageOptions? packageOptions = null;
            if (pack)
            {
                var prefix = parseResult.GetRequiredValue(packageIdPrefixOption).TrimEnd('.');
                if (!PackageIdRegex().IsMatch(prefix))
                {
                    await error.WriteLineAsync($"'{prefix}' is not a valid prefix of package identifiers: use letters, digits, '.', '-' and '_'.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    return 1;
                }

                PackageContent content;
                switch (parseResult.GetRequiredValue(packageContentOption).ToLowerInvariant())
                {
                    case "assembly":
                        content = PackageContent.Assembly;
                        break;
                    case "files":
                        content = PackageContent.Files;
                        break;
                    default:
                        await error.WriteLineAsync($"Unknown package content: {parseResult.GetValue(packageContentOption)}. The package contents are assembly and files.".AsMemory(), cancellationToken).ConfigureAwait(false);
                        return 1;
                }

                // SQLite reads a GeoPackage from a file: it cannot be read from the memory of an assembly
                if (content is PackageContent.Assembly && format is GeoPackageMapFormat)
                {
                    await error.WriteLineAsync("A GeoPackage map needs a file: use --package-content files to pack gpkg bundles.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(parseResult.GetValue(packageTargetFrameworkOption)) || string.IsNullOrWhiteSpace(parseResult.GetValue(packageSdkOption)))
                {
                    await error.WriteLineAsync("--package-target-framework and --package-sdk cannot be empty.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    return 1;
                }

                packageOptions = new PackageOptions(FullPath.FromPath(parseResult.GetRequiredValue(packageOutputOption)), prefix, parseResult.GetRequiredValue(packageVersionOption), parseResult.GetValue(authorsOption) ?? prefix, content)
                {
                    Sdk = parseResult.GetRequiredValue(packageSdkOption),
                    TargetFrameworks = parseResult.GetRequiredValue(packageTargetFrameworkOption),
                    ProjectDirectory = parseResult.GetValue(packageProjectOutputOption) is { } projectOutput ? FullPath.FromPath(projectOutput) : null,
                };
            }

            var downloader = await CreateDownloaderAsync(parseResult.GetValue(pinsOption), parseResult.GetValue(cacheOption), update: false, output, cancellationToken).ConfigureAwait(false);
            var sources = await SourceSet.LoadAsync(downloader, output, cancellationToken).ConfigureAwait(false);
            List<Region> selected;
            if (all)
            {
                selected = [.. sources.Regions];
            }
            else
            {
                var unknown = regionIds.Where(id => !sources.Regions.Any(region => Matches(region, id))).ToList();
                if (unknown.Count > 0)
                {
                    await error.WriteLineAsync(("Unknown regions: " + string.Join(", ", unknown) + ". " + RegionHelp).AsMemory(), cancellationToken).ConfigureAwait(false);
                    return 1;
                }

                selected = [.. sources.Regions.Where(region => regionIds.Any(id => Matches(region, id)))];
            }

            var previews = parseResult.GetValue(previewsOption) is { } previewsPath ? FullPath.FromPath(previewsPath) : (FullPath?)null;
            return await BuildAsync(selected, sources, downloader, format, layers, FullPath.FromPath(parseResult.GetRequiredValue(outputOption)), previews, packageOptions, output, cancellationToken).ConfigureAwait(false);
        });
        return command;
    }

    private static async Task<int> BuildAsync(List<Region> selected, SourceSet sources, Downloader downloader, MapFormat format, IReadOnlySet<MapLayer> layers, FullPath outputDirectory, FullPath? previews, PackageOptions? packageOptions, TextWriter output, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var failures = new List<string>();
        var built = 0;
        var projects = new List<FullPath>();
        // The projects of the assemblies are in a temporary folder, deleted once packed, unless the options give their folder
        var projectsDirectory = packageOptions is { Content: PackageContent.Assembly } ? packageOptions.ProjectDirectory ?? FullPath.GetTempPath() / "Meziantou.Framework.Geo.Maps.Tool" / Guid.NewGuid().ToString("N") : FullPath.Empty;
        var temporaryProjects = packageOptions is { ProjectDirectory: null };
        foreach (var region in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regionWatch = Stopwatch.StartNew();
            try
            {
                var regionLayers = RegionBuilder.BuildLayers(region, sources, layers, out var extent);
                if (regionLayers.Count == 0)
                {
                    await output.WriteLineAsync($"  skip  {region.Key}: no data ({regionWatch.Elapsed.TotalSeconds:F1}s)".AsMemory(), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var path = RegionBuilder.Write(region, regionLayers, extent, format, outputDirectory, downloader.Date);
                if (previews is { } previewsDirectory)
                {
                    PreviewRenderer.Render(region, regionLayers, extent, previewsDirectory);
                }

                if (packageOptions is { Content: PackageContent.Files })
                {
                    NuGetPackageWriter.Write(region, format, path, regionLayers.Keys, packageOptions);
                }
                else if (packageOptions is { Content: PackageContent.Assembly })
                {
                    projects.Add(MapProjectWriter.Write(region, format, path, packageOptions, projectsDirectory, isolated: temporaryProjects));
                }

                built++;
                await output.WriteLineAsync($"  done  {region.Key}: {string.Join(", ", regionLayers.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value.Count}"))} ({regionWatch.Elapsed.TotalSeconds:F1}s)".AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A region that fails does not stop the others: the failures are listed at the end
                failures.Add(region.Key);
                await output.WriteLineAsync($"  FAIL  {region.Key}: {exception}".AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        await output.WriteLineAsync($"Built {built} regions ({format.Name}) in {watch.Elapsed.TotalSeconds:F1}s, to {outputDirectory}".AsMemory(), cancellationToken).ConfigureAwait(false);

        // The assemblies of the packages are built at the end, in one build of the .NET SDK
        if (projects.Count > 0)
        {
            var packWatch = Stopwatch.StartNew();
            await output.WriteLineAsync($"Packing {projects.Count} assemblies with the .NET SDK...".AsMemory(), cancellationToken).ConfigureAwait(false);
            var (exitCode, packOutput) = await DotNetCli.PackAsync(projects, projectsDirectory, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                await output.WriteLineAsync(packOutput.AsMemory(), cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync($"The .NET SDK failed to pack the assemblies (exit code {exitCode}). The projects are in {projectsDirectory}".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            if (temporaryProjects)
            {
                Directory.Delete(projectsDirectory, recursive: true);
            }

            await output.WriteLineAsync($"Packed {projects.Count} packages in {packWatch.Elapsed.TotalSeconds:F1}s, to {packageOptions!.OutputDirectory}".AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        if (failures.Count > 0)
        {
            await output.WriteLineAsync(("Failed: " + string.Join(", ", failures)).AsMemory(), cancellationToken).ConfigureAwait(false);
            return 1;
        }

        return 0;
    }

    private static Command CreateListCommand()
    {
        var pinsOption = CreatePinsOption();
        var cacheOption = CreateCacheOption();
        var command = new Command("list", "Lists the regions: their identifier, their key (the name of their bundle), their ISO 3166-1 code and their continent");
        command.Options.Add(pinsOption);
        command.Options.Add(cacheOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            var downloader = await CreateDownloaderAsync(parseResult.GetValue(pinsOption), parseResult.GetValue(cacheOption), update: false, output, cancellationToken).ConfigureAwait(false);
            var countryLevels = await CountryLevelsSource.LoadAsync(downloader).ConfigureAwait(false);
            var regions = await Regions.LoadAsync(downloader, downloader.CldrVersion!, countryLevels).ConfigureAwait(false);
            // The world and the continents, then the countries and territories by identifier
            var rows = regions.OrderBy(region => region.Parent is not null).ThenBy(region => region.Parent is null ? "" : region.Id, StringComparer.Ordinal).Select(region => (region.Id, region.Key, Iso: region.Parent is null ? "" : string.Join(',', region.Iso), Continent: region.Parent ?? "")).ToList();
            var widths = (Id: rows.Max(row => row.Id.Length), Key: rows.Max(row => row.Key.Length), Iso: Math.Max(3, rows.Max(row => row.Iso.Length)));
            await output.WriteLineAsync($"{"Id".PadRight(widths.Id)}  {"Key".PadRight(widths.Key)}  {"ISO".PadRight(widths.Iso)}  Continent".AsMemory(), cancellationToken).ConfigureAwait(false);
            foreach (var (id, key, iso, continent) in rows)
            {
                await output.WriteLineAsync($"{id.PadRight(widths.Id)}  {key.PadRight(widths.Key)}  {iso.PadRight(widths.Iso)}  {continent}".TrimEnd().AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return 0;
        });
        return command;
    }

    private static Command CreateUpdatePinsCommand()
    {
        var pinsOption = new Option<string>("--pins") { Description = "The file of pins to write; its date is kept when the pins do not change", Required = true };
        var cacheOption = CreateCacheOption();
        var command = new Command("update-pins", "Downloads the latest versions of the sources, and pins them in a file to use with --pins");
        command.Options.Add(pinsOption);
        command.Options.Add(cacheOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            var pinsPath = FullPath.FromPath(parseResult.GetRequiredValue(pinsOption));
            var downloader = await CreateDownloaderAsync(File.Exists(pinsPath) ? pinsPath.Value : null, parseResult.GetValue(cacheOption), update: true, output, cancellationToken).ConfigureAwait(false);

            // The latest commit of Natural Earth and the latest release of CLDR, then every file at these versions
            using (var commit = JsonDocument.Parse(await SharedHttpClient.InstanceWithAutoRedirect.GetStringAsync(new Uri("https://api.github.com/repos/nvkelso/natural-earth-vector/commits/master"), cancellationToken).ConfigureAwait(false)))
            {
                downloader.NaturalEarthCommit = commit.RootElement.GetProperty("sha").GetString();
            }

            using (var release = JsonDocument.Parse(await SharedHttpClient.InstanceWithAutoRedirect.GetStringAsync(new Uri("https://api.github.com/repos/unicode-org/cldr-json/releases/latest"), cancellationToken).ConfigureAwait(false)))
            {
                downloader.CldrVersion = release.RootElement.GetProperty("tag_name").GetString();
            }

            await SourceSet.LoadAsync(downloader, output, cancellationToken).ConfigureAwait(false);
            var changed = await downloader.SavePinsAsync(pinsPath, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync(((changed ? "Updated " : "Up to date: ") + pinsPath).AsMemory(), cancellationToken).ConfigureAwait(false);
            return 0;
        });
        return command;
    }

    private static async Task<Downloader> CreateDownloaderAsync(string? pinsPath, string? cachePath, bool update, TextWriter output, CancellationToken cancellationToken)
    {
        var cache = cachePath is null ? FullPath.FromPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Meziantou.Framework.Geo.Maps.Tool", "cache")) : FullPath.FromPath(cachePath);
        if (pinsPath is null)
            return Downloader.Create(update ? null : Downloader.GetEmbeddedPins(), "the pins of the tool", cache, update, output);

        var path = FullPath.FromPath(pinsPath);
        return Downloader.Create(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), path.Value, cache, update, output);
    }

    // A layer is named as the files of the bundles name it (states-provinces), or as MapLayer does (StatesProvinces)
    internal static HashSet<MapLayer>? GetLayers(string? value, out string? unknown)
    {
        unknown = null;
        if (value is null)
            return Enum.GetValues<MapLayer>().ToHashSet();

        var layers = new HashSet<MapLayer>();
        foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var layer = Enum.GetValues<MapLayer>().Cast<MapLayer?>().FirstOrDefault(layer =>
                string.Equals(MapFormat.GetLayerName(layer!.Value), name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(layer.Value.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (layer is null)
            {
                unknown = name;
                return null;
            }

            layers.Add(layer.Value);
        }

        if (layers.Count == 0)
        {
            unknown = value;
            return null;
        }

        return layers;
    }

    internal static MapFormat? GetFormat(string name) => name.ToLowerInvariant() switch
    {
        "fgb" or "flatgeobuf" => MapFormat.FlatGeobuf,
        "geojson" => MapFormat.GeoJson,
        "gpkg" or "geopackage" => GeoPackageMapFormat.Instance,
        _ => null,
    };

    internal static bool Matches(Region region, string id) =>
        string.Equals(region.Id, id, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region.Key, id, StringComparison.OrdinalIgnoreCase) ||
        (region.Parent is not null && region.Iso is [var code] && string.Equals(code, id, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PackageIdRegex();
}
