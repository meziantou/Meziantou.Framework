using System.Runtime.CompilerServices;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>
/// Reads and writes FlatGeobuf files (version 3): a binary format of features, encoded with FlatBuffers, with an optional
/// spatial index that lets a reader get the features of a box without decoding the others.
/// </summary>
/// <remarks>
/// <para>
/// The reader reads the points, lines, polygons, their multi-geometries and the geometry collections, with their altitudes
/// (Z) and their measures (M, a NaN value being no measure); the T and TM values are dropped, as the format does not define
/// their meaning, and the curves, surfaces and triangles throw a <see cref="FormatException"/>.
/// The collection is named after the name of the header. The property values have the .NET type of their column: a
/// <see cref="sbyte"/>, <see cref="byte"/>, <see cref="bool"/>, <see cref="short"/>, <see cref="ushort"/>, <see cref="int"/>,
/// <see cref="uint"/>, <see cref="long"/>, <see cref="ulong"/>, <see cref="float"/>, <see cref="double"/>, a
/// <see cref="string"/>, a <see cref="System.Text.Json.JsonElement"/> for a JSON column (or the text when it is not valid
/// JSON), a <see cref="DateTimeOffset"/> for a date-time column (a time without offset is UTC, and a value that cannot be
/// parsed is kept as text) and an array of bytes for a binary column. The features have no identifier. The coordinate
/// system of the header is not read: the coordinates are expected to be longitudes and latitudes.
/// </para>
/// <para>
/// The writer writes the header with the name of the collection, its bounds, the coordinate system EPSG:4326 (WGS 84),
/// one geometry type when every geometry has the same type (else each geometry gets its type), and Z values when a
/// position has an altitude (the positions without altitude get 0), and M values when a position has a measure (the
/// positions without measure get NaN). The times of the positions are not written. The columns are the property names in the order
/// they are first seen; a column gets the type of its values, <see cref="long"/> for integers of different types,
/// <see cref="double"/> for numbers of different types, and the text of the values for other mixes. A date, a
/// <see cref="DateTime"/> or a <see cref="DateTimeOffset"/> is a date-time column in ISO 8601, to the millisecond (GDAL
/// reads at most 32 characters), an array of bytes a binary
/// column, a <see cref="System.Text.Json.JsonElement"/> object or array a JSON column, and other values a string column
/// of their text. A <see langword="null"/> value is not written. A feature without geometry, or with an empty one, is
/// written without geometry. The identifiers of the features are not written.
/// </para>
/// <para>
/// With the spatial index, the default, the features are written along a Hilbert curve, the order of the index, and not
/// in the order of the collection; the index is not written when a feature has no geometry.
/// </para>
/// </remarks>
public static class FlatGeobuf
{
    /// <summary>Reads a FlatGeobuf file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features, in a collection named after the name of the header, else after the file, without its extension.</returns>
    /// <exception cref="FormatException">The file is not valid FlatGeobuf, or has a geometry that is not supported.</exception>
    public static GeoFeatureCollection Read(string path, FlatGeobufReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var features = FlatGeobufReader.Read(File.ReadAllBytes(path), options?.Bounds);
        features.Name ??= Path.GetFileNameWithoutExtension(path);
        return features;
    }

    /// <summary>Reads FlatGeobuf data from a stream.</summary>
    /// <param name="stream">The data. It is read to its end and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features.</returns>
    /// <exception cref="FormatException">The data is not valid FlatGeobuf, or has a geometry that is not supported.</exception>
    public static GeoFeatureCollection Read(Stream stream, FlatGeobufReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FlatGeobufReader.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options?.Bounds);
    }

    /// <summary>Reads FlatGeobuf data.</summary>
    /// <param name="data">The data.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features.</returns>
    /// <exception cref="FormatException">The data is not valid FlatGeobuf, or has a geometry that is not supported.</exception>
    public static GeoFeatureCollection Read(ReadOnlyMemory<byte> data, FlatGeobufReaderOptions? options = null) => FlatGeobufReader.Read(data.Span, options?.Bounds);

    /// <summary>Reads FlatGeobuf data.</summary>
    /// <param name="data">The data. The features do not refer to it once the method returns: the data can be memory that the caller frees, or the memory of a resource of an assembly.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features.</returns>
    /// <exception cref="FormatException">The data is not valid FlatGeobuf, or has a geometry that is not supported.</exception>
    public static GeoFeatureCollection Read(ReadOnlySpan<byte> data, FlatGeobufReaderOptions? options = null) => FlatGeobufReader.Read(data, options?.Bounds);

    /// <summary>
    /// Reads the features of a FlatGeobuf file one at a time, with bounded memory: only the feature being read is kept in
    /// memory. With <see cref="FlatGeobufReaderOptions.Bounds"/> and a spatial index, only the nodes of the index that the
    /// search visits and the features it finds are read from the file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the file. The file is opened when the enumeration starts, and closed when it ends.</returns>
    /// <exception cref="FormatException">The file is not valid FlatGeobuf, or has a geometry that is not supported. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(string path, FlatGeobufReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileFeaturesAsync(path, options?.Bounds, cancellationToken);
    }

    /// <summary>
    /// Reads the features of FlatGeobuf data from a stream, one at a time, with bounded memory: only the feature being read
    /// is kept in memory. With <see cref="FlatGeobufReaderOptions.Bounds"/>, a spatial index and a seekable stream, only
    /// the nodes of the index that the search visits and the features it finds are read; a stream that cannot seek is read
    /// to its end, and the features outside of the box are skipped.
    /// </summary>
    /// <param name="stream">The data. It is read from its current position as the features are enumerated, and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the data.</returns>
    /// <exception cref="FormatException">The data is not valid FlatGeobuf, or has a geometry that is not supported. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(Stream stream, FlatGeobufReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FlatGeobufStreamReader.ReadAsync(stream, options?.Bounds, cancellationToken);
    }

    /// <summary>Writes features as a FlatGeobuf file, replacing the existing one.</summary>
    /// <param name="features">The features.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static void Write(GeoFeatureCollection features, string path, FlatGeobufWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentException.ThrowIfNullOrEmpty(path);
        GeoValidation.EnsureFinite(features, nameof(features));
        using var stream = File.Create(path);
        FlatGeobufWriter.Write(features, stream, options ?? new FlatGeobufWriterOptions());
    }

    /// <summary>Writes features as FlatGeobuf data to a stream.</summary>
    /// <param name="features">The features.</param>
    /// <param name="stream">The stream. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static void Write(GeoFeatureCollection features, Stream stream, FlatGeobufWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(stream);
        GeoValidation.EnsureFinite(features, nameof(features));
        FlatGeobufWriter.Write(features, stream, options ?? new FlatGeobufWriterOptions());
    }

    private static async IAsyncEnumerable<GeoFeature> ReadFileFeaturesAsync(string path, GeoBounds? bounds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var feature in FlatGeobufStreamReader.ReadAsync(stream, bounds, cancellationToken).ConfigureAwait(false))
            {
                yield return feature;
            }
        }
    }

    /// <summary>Writes features as FlatGeobuf data.</summary>
    /// <param name="features">The features.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The data.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static byte[] Write(GeoFeatureCollection features, FlatGeobufWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(features, stream, options);
        return stream.ToArray();
    }
}
