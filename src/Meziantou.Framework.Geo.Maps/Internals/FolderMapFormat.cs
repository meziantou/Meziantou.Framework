using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Geo.Maps.Internals;

/// <summary>A map stored as a folder named after its region: a file per layer, and a <c>metadata.json</c> file.</summary>
internal sealed class FolderMapFormat : MapFormat
{
    public const string MetadataFileName = "metadata.json";

    private readonly Encoding _encoding;

    public FolderMapFormat(Encoding encoding) => _encoding = encoding;

    public enum Encoding
    {
        FlatGeobuf,
        GeoJson,
    }

    public override string Name => _encoding is Encoding.FlatGeobuf ? "FlatGeobuf" : "GeoJSON";

    private string Extension => _encoding is Encoding.FlatGeobuf ? ".fgb" : ".geojson";

    public override string GetPath(string directory, string region)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(region);
        return Path.Combine(directory, region);
    }

    // A folder is a map of this format when it has a layer in this format: a folder of another format, or an unrelated
    // folder, is not
    public override bool TryGetRegion(string path, [NotNullWhen(true)] out string? region)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Directory.Exists(path) && Enum.GetValues<MapLayer>().Any(layer => File.Exists(GetLayerPath(path, layer))))
        {
            region = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            return region.Length > 0;
        }

        region = null;
        return false;
    }

    public override Map Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var region = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var metadataPath = Path.Combine(path, MetadataFileName);
        MapMetadata metadata;
        try
        {
            metadata = File.Exists(metadataPath) ? ParseMetadata(region, File.ReadAllText(metadataPath)) : new MapMetadata(region);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"The file '{metadataPath}' is not valid.", exception);
        }

        return new FolderMap(this, path, metadata);
    }

    public override void Write(string path, MapMetadata metadata, IReadOnlyDictionary<MapLayer, GeoFeatureCollection> layers)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layers);

        // The folder formats share the folder and its metadata.json: the layers of the other folder format are removed, so
        // that the folder holds one map
        Directory.CreateDirectory(path);
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            var layerPath = GetLayerPath(path, layer);
            File.Delete(Path.ChangeExtension(layerPath, _encoding is Encoding.FlatGeobuf ? ".geojson" : ".fgb"));
            if (!layers.TryGetValue(layer, out var features))
            {
                File.Delete(layerPath);
                continue;
            }

            if (_encoding is Encoding.FlatGeobuf)
            {
                Geo.FlatGeobuf.Write(features, layerPath, new FlatGeobufWriterOptions
                {
                    Name = layer.ToString(),
                    Title = (metadata.Name ?? metadata.Region) + " - " + layer,
                    Description = metadata.Attribution,
                });
            }
            else
            {
                using var stream = File.Create(layerPath);
                Geo.GeoJson.Write(features, stream);
            }
        }

        File.WriteAllText(Path.Combine(path, MetadataFileName), FormatMetadata(metadata, layers));
    }

    public string GetLayerPath(string directory, MapLayer layer) => Path.Combine(directory, GetLayerName(layer) + Extension);

    public IEnumerable<GeoFeature> Read(string path, GeoBounds? bounds)
    {
        if (_encoding is Encoding.FlatGeobuf)
            return Geo.FlatGeobuf.Read(path, new FlatGeobufReaderOptions { Bounds = bounds });

        // GeoJSON has no index: the whole layer is read, and the map keeps the features of the box
        using var stream = File.OpenRead(path);
        return Geo.GeoJson.Read(stream);
    }
}
