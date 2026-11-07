using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>
/// Writes the projects of the map packages of this repository (the update-projects command), in <c>src/Meziantou.Framework.Geo.Maps.Data</c>: a project per
/// region, with its version, the SHA-256 of its layers and the code that opens it, but not the layers, which are written
/// to the <c>data</c> folder, where the projects embed them from.
/// </summary>
/// <remarks>
/// A new project has the version <c>1.0.0</c>, and its version is incremented (<c>1.0.1</c>) only when its project or its
/// code changes: when its layers change, or when the tool writes them differently. The date of the data of a map is then the date of the pins; a map whose layers do
/// not change keeps its date, so that its code does not change either.
/// </remarks>
internal static class RepositoryProjects
{
    public const string IdPrefix = "Meziantou.Framework.Geo.Maps.Data";

    /// <summary>The version of a new project.</summary>
    public const string InitialVersion = "1.0.0";

    /// <summary>Writes the layers and the projects of every region, and deletes the projects of the regions that no longer exist.</summary>
    /// <param name="sources">The sources of the regions.</param>
    /// <param name="date">The date of the pins of the sources.</param>
    /// <param name="directory">The folder of the projects, <c>src/Meziantou.Framework.Geo.Maps.Data</c>.</param>
    /// <param name="output">The log.</param>
    /// <param name="cancellationToken">The token to stop.</param>
    /// <returns>The keys of the regions whose project was added, updated or deleted.</returns>
    public static async Task<RepositoryProjectsResult> UpdateAsync(SourceSet sources, DateTimeOffset date, FullPath directory, TextWriter output, CancellationToken cancellationToken)
    {
        var dataDirectory = directory / "data";
        var format = MapFormat.FlatGeobuf;
        var layersToBuild = Enum.GetValues<MapLayer>().ToHashSet();
        var added = new List<string>();
        var updated = new List<string>();
        var unchanged = new List<string>();
        var projectNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var region in sources.Regions.OrderBy(region => region.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layers = RegionBuilder.BuildLayers(region, sources, layersToBuild, out var extent);
            if (layers.Count == 0)
                continue;

            var id = IdPrefix + "." + region.Key;
            projectNames.Add(id);
            var projectDirectory = directory / id;
            var projectPath = projectDirectory / (id + ".csproj");
            var codePath = projectDirectory / (region.Key + "Map.g.cs");
            var previous = File.Exists(projectPath) ? ReadProject(projectPath) : null;

            var mapPath = RegionBuilder.Write(region, layers, extent, format, dataDirectory, date);
            var hashes = Directory.EnumerateFiles(mapPath, "*.fgb")
                .Order(StringComparer.Ordinal)
                .ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), StringComparer.Ordinal);

            // A map whose layers do not change keeps the date of its data
            var dataDate = date;
            if (previous is not null && previous.DataDate is { } previousDate && previous.Layers.Count == hashes.Count && hashes.All(pair => previous.Layers.TryGetValue(pair.Key, out var hash) && string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase)))
            {
                dataDate = previousDate;
                if (dataDate != date)
                {
                    mapPath = RegionBuilder.Write(region, layers, extent, format, dataDirectory, dataDate);
                }
            }

            var map = format.Open(mapPath);
            var description = MapProjectWriter.GetDescription(region, format, map.Layers);
            var code = MapProjectWriter.GenerateCode(region, format, map, IdPrefix, description, sharedReader: true);
            var version = previous?.Version ?? InitialVersion;
            var project = GenerateProject(region, version, dataDate, description, hashes);
            if (previous is not null)
            {
                if (project == previous.Text && File.Exists(codePath) && code == await File.ReadAllTextAsync(codePath, cancellationToken).ConfigureAwait(false))
                {
                    unchanged.Add(region.Key);
                    continue;
                }

                version = GetNextVersion(previous.Version);
                project = GenerateProject(region, version, dataDate, description, hashes);
            }

            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(projectPath, project, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(codePath, code, cancellationToken).ConfigureAwait(false);

            // The project holds the csproj and the code only: the files of an older version of the generator are removed
            foreach (var file in Directory.EnumerateFiles(projectDirectory))
            {
                if (FullPath.FromPath(file) != projectPath && FullPath.FromPath(file) != codePath)
                {
                    File.Delete(file);
                }
            }

            (previous is null ? added : updated).Add(region.Key);
            await output.WriteLineAsync($"  {(previous is null ? "add" : "update")}  {id} {version}".AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        // The projects of the regions that no longer exist, or no longer have data
        var removed = new List<string>();
        if (Directory.Exists(directory))
        {
            foreach (var projectDirectory in Directory.EnumerateDirectories(directory, IdPrefix + ".*"))
            {
                var name = Path.GetFileName(projectDirectory);
                if (!projectNames.Contains(name))
                {
                    Directory.Delete(projectDirectory, recursive: true);
                    removed.Add(name[(IdPrefix.Length + 1)..]);
                    await output.WriteLineAsync($"  remove  {name}".AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await output.WriteLineAsync($"Map projects: {added.Count} added, {updated.Count} updated, {removed.Count} removed, {unchanged.Count} unchanged".AsMemory(), cancellationToken).ConfigureAwait(false);
        return new RepositoryProjectsResult(added, updated, removed, unchanged);
    }

    /// <summary>Gets the version that follows <paramref name="previous"/>: its patch number incremented, <c>1.0.0</c> to <c>1.0.1</c>.</summary>
    public static string GetNextVersion(string previous)
    {
        var current = Version.Parse(previous);
        return string.Create(CultureInfo.InvariantCulture, $"{current.Major}.{current.Minor}.{Math.Max(current.Build, 0) + 1}");
    }

    private static string GenerateProject(Region region, string version, DateTimeOffset dataDate, string description, IReadOnlyDictionary<string, string> hashes)
    {
        var builder = new StringBuilder();
        builder.Append("<Project Sdk=\"Meziantou.NET.Sdk\">\n\n");
        builder.Append("  <!-- Generated by the update-projects command of tools/Meziantou.Framework.Geo.Maps.Tool: the layers are in ../data/").Append(Escape(region.Key)).Append(", which is not committed -->\n");
        builder.Append("  <PropertyGroup>\n");
        builder.Append("    <TargetFrameworks>$(MinimumTargetFramework)</TargetFrameworks>\n");
        builder.Append("    <MapRegion>").Append(Escape(region.Key)).Append("</MapRegion>\n");
        builder.Append("    <MapDataDate>").Append(dataDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</MapDataDate>\n");
        builder.Append("    <Version>").Append(Escape(version)).Append("</Version>\n");
        builder.Append("    <Description>").Append(Escape(description)).Append("</Description>\n");
        builder.Append("    <IsTrimmable>true</IsTrimmable>\n");
        builder.Append("    <SkipAutoVersionUpdates>true</SkipAutoVersionUpdates>\n");
        builder.Append("    <IncludeInRepositoryReadme>false</IncludeInRepositoryReadme>\n");
        builder.Append("  </PropertyGroup>\n\n");
        builder.Append("  <ItemGroup>\n");
        foreach (var (name, hash) in hashes)
        {
            builder.Append("    <MapLayer Include=\"").Append(Escape(name)).Append("\" Sha256=\"").Append(hash).Append("\" />\n");
        }

        builder.Append("  </ItemGroup>\n\n");
        builder.Append("</Project>\n");
        return builder.ToString();
    }

    private static PreviousProject ReadProject(FullPath path)
    {
        var text = File.ReadAllText(path);
        var document = XDocument.Parse(text);
        var version = document.Descendants("Version").FirstOrDefault()?.Value ?? throw new InvalidOperationException($"{path} has no version.");
        DateTimeOffset? dataDate = document.Descendants("MapDataDate").FirstOrDefault()?.Value is { } value
            ? DateTimeOffset.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : null;
        var layers = document.Descendants("MapLayer").ToDictionary(element => (string)element.Attribute("Include")!, element => (string?)element.Attribute("Sha256") ?? "", StringComparer.Ordinal);
        return new PreviousProject(text, version, dataDate, layers);
    }

    private static string Escape(string text) => SecurityElement.Escape(text);

    private sealed record PreviousProject(string Text, string Version, DateTimeOffset? DataDate, Dictionary<string, string> Layers);
}
