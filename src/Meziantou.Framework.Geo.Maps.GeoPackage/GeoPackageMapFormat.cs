using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Geo.Maps.GeoPackage.Internals;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Maps.GeoPackage;

/// <summary>
/// The format that stores a map as one GeoPackage file named after its region, such as <c>France.gpkg</c>: a feature
/// table per layer, with an R-tree spatial index, that QGIS, GDAL and ArcGIS open.
/// </summary>
/// <remarks>
/// <para>
/// The file is a GeoPackage 1.4 in WGS 84 (EPSG:4326). A layer is the table named after <see cref="MapFormat.GetLayerName"/>,
/// with hyphens replaced by underscores, such as <c>states_provinces</c>: its identifier is the name of the region and
/// of the layer, and its description the attribution. The metadata of the map, the JSON of the <c>metadata.json</c> file
/// of the folder formats, is in the metadata extension, with the scope of the whole file.
/// </para>
/// <para>
/// The property values get the type of their column: a <see cref="bool"/> for <c>BOOLEAN</c>, an <see cref="int"/> for
/// <c>MEDIUMINT</c>, a <see cref="long"/> for <c>INTEGER</c>, a <see cref="double"/> for <c>DOUBLE</c>, a
/// <see cref="DateTimeOffset"/> for <c>DATETIME</c>, an array of bytes for <c>BLOB</c> and a <see cref="string"/> for
/// <c>TEXT</c>. The writer gives a column the type of its values, as the FlatGeobuf writer does, and the text of the
/// values for other mixes.
/// </para>
/// <para>Reading a file needs the native SQLite library of Microsoft.Data.Sqlite, with its R-tree module.</para>
/// </remarks>
public sealed class GeoPackageMapFormat : MapFormat
{
    internal const int ApplicationId = 0x47504B47; // "GPKG"
    internal const int UserVersion = 10400; // 1.4.0
    internal const string MetadataStandardUri = "urn:meziantou:geo-maps:metadata";

    private const string Extension = ".gpkg";

    private GeoPackageMapFormat()
    {
    }

    /// <summary>Gets the GeoPackage format.</summary>
    public static GeoPackageMapFormat Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "GeoPackage";

    /// <inheritdoc/>
    public override string GetPath(string directory, string region)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(region);
        return Path.Combine(directory, region + Extension);
    }

    /// <inheritdoc/>
    public override bool TryGetRegion(string path, [NotNullWhen(true)] out string? region)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            region = Path.GetFileNameWithoutExtension(path);
            return region.Length > 0;
        }

        region = null;
        return false;
    }

    /// <inheritdoc/>
    /// <exception cref="FormatException">The file is not a GeoPackage, or its metadata is not valid.</exception>
    public override Map Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var region = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var connection = OpenConnection(path, SqliteOpenMode.ReadOnly);
            var json = GeoPackageReader.ReadMetadata(connection);
            var metadata = json is null ? new MapMetadata(region) : ParseMetadata(region, json);
            return new GeoPackageMap(path, metadata, GeoPackageReader.ReadLayers(connection));
        }
        catch (SqliteException exception)
        {
            throw new FormatException($"The file '{path}' is not a valid GeoPackage file.", exception);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"The file '{path}' is not a valid map: {exception.Message}", exception);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The file is replaced. Its dates, the last change of the layers, are <see cref="MapMetadata.LastModified"/>, else
    /// the current date: with the same date, the same map gives the same file.
    /// </remarks>
    /// <exception cref="ArgumentException">A property cannot be a column of a table: its name is <c>fid</c> or <c>geom</c>, or differs from another one only by case.</exception>
    public override void Write(string path, MapMetadata metadata, IReadOnlyDictionary<MapLayer, GeoFeatureCollection> layers)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layers);
        GeoPackageWriter.Write(path, metadata, FormatMetadata(metadata, layers), FormatDate(metadata.LastModified ?? DateTimeOffset.UtcNow), layers);
    }

    internal static string GetTableName(MapLayer layer) => GetLayerName(layer).Replace('-', '_');

    internal static string FormatDateTime(DateTimeOffset date) => FormatDate(date);

    // Without pooling, the file is closed with the connection, so that it can be replaced or deleted
    internal static SqliteConnection OpenConnection(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
