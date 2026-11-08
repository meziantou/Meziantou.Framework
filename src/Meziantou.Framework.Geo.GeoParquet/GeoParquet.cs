using System.Runtime.CompilerServices;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>
/// Reads GeoParquet files (versions 1.0 and 1.1): Parquet files whose <c>geo</c> metadata describes a geometry column
/// encoded as WKB, as GDAL, GeoPandas, DuckDB and the cloud data catalogs write them.
/// </summary>
/// <remarks>
/// <para>
/// The geometries are read as <see cref="WellKnownBinary"/> reads them, with their altitudes (Z) and without the M values.
/// A <see langword="null"/> or empty geometry gives a feature without geometry. The geometry columns encoded with the
/// native (GeoArrow) encodings throw a <see cref="FormatException"/>. The coordinate system of the column is not read:
/// the coordinates are expected to be longitudes and latitudes (OGC:CRS84, the default of GeoParquet), and are not
/// converted otherwise.
/// </para>
/// <para>
/// The properties are the other top-level columns, with the .NET type of the column, such as an <see cref="int"/>, a
/// <see cref="long"/>, a <see cref="double"/>, a <see cref="string"/>, a <see cref="DateTime"/> or an array of bytes. A
/// <see langword="null"/> value is not a property. The columns of structures, lists and maps, such as the bbox covering
/// column, are not read. The features have no identifier.
/// </para>
/// <para>
/// The file is read one row group at a time: the memory holds the columns of a row group, not the whole file. With
/// <see cref="GeoParquetReaderOptions.Bounds"/>, the row groups whose bbox covering statistics are outside of the box
/// are skipped.
/// </para>
/// </remarks>
public static class GeoParquet
{
    /// <summary>Reads the features of a GeoParquet file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the file, in a collection named after the file, without its extension.</returns>
    /// <exception cref="ArgumentException">The file has no geometry column named <see cref="GeoParquetReaderOptions.GeometryColumn"/>.</exception>
    /// <exception cref="FormatException">The file is not a valid GeoParquet file, its geometry column is not encoded as WKB, or a geometry is not valid.</exception>
    public static async Task<GeoFeatureCollection> ReadAsync(string path, GeoParquetReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var features = await ReadAsync(stream, options, cancellationToken).ConfigureAwait(false);
            features.Name = Path.GetFileNameWithoutExtension(path);
            return features;
        }
    }

    /// <summary>Reads the features of GeoParquet data from a stream.</summary>
    /// <param name="stream">The data. It must be seekable, to read the metadata at its end. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the data.</returns>
    /// <exception cref="ArgumentException">The data has no geometry column named <see cref="GeoParquetReaderOptions.GeometryColumn"/>.</exception>
    /// <exception cref="FormatException">The data is not valid GeoParquet, its geometry column is not encoded as WKB, or a geometry is not valid.</exception>
    public static async Task<GeoFeatureCollection> ReadAsync(Stream stream, GeoParquetReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var features = new GeoFeatureCollection();
        await foreach (var feature in GeoParquetReader.ReadAsync(stream, options, cancellationToken).ConfigureAwait(false))
        {
            features.Add(feature);
        }

        return features;
    }

    /// <summary>
    /// Reads the features of a GeoParquet file one row group at a time, with bounded memory: the columns of a row group
    /// are read when its features are enumerated.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the file. The file is opened when the enumeration starts, and closed when it ends.</returns>
    /// <exception cref="ArgumentException">The file has no geometry column named <see cref="GeoParquetReaderOptions.GeometryColumn"/>.</exception>
    /// <exception cref="FormatException">The file is not a valid GeoParquet file, its geometry column is not encoded as WKB, or a geometry is not valid. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(string path, GeoParquetReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileFeaturesAsync(path, options, cancellationToken);
    }

    /// <summary>
    /// Reads the features of GeoParquet data from a stream one row group at a time, with bounded memory: the columns of a
    /// row group are read when its features are enumerated.
    /// </summary>
    /// <param name="stream">The data. It must be seekable, to read the metadata at its end. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the data.</returns>
    /// <exception cref="ArgumentException">The data has no geometry column named <see cref="GeoParquetReaderOptions.GeometryColumn"/>.</exception>
    /// <exception cref="FormatException">The data is not valid GeoParquet, its geometry column is not encoded as WKB, or a geometry is not valid. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(Stream stream, GeoParquetReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return GeoParquetReader.ReadAsync(stream, options, cancellationToken);
    }

    private static async IAsyncEnumerable<GeoFeature> ReadFileFeaturesAsync(string path, GeoParquetReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var feature in GeoParquetReader.ReadAsync(stream, options, cancellationToken).ConfigureAwait(false))
            {
                yield return feature;
            }
        }
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous);
}
