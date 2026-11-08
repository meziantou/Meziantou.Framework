using System.Runtime.CompilerServices;
using Meziantou.Framework.Geo.Internals;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo;

/// <summary>
/// Reads the feature tables of GeoPackage files (OGC GeoPackage 1.0 to 1.4): SQLite databases that GDAL, QGIS and ArcGIS
/// write, with one table of features per layer and an optional R-tree spatial index.
/// </summary>
/// <remarks>
/// <para>
/// The geometries are the standard GeoPackage geometries: a header, then the WKB of the geometry, read as
/// <see cref="WellKnownBinary"/> reads it, with the altitudes (Z) and the measures (M). An empty geometry and a
/// <see langword="null"/> one give a feature without geometry; the extended geometries of extensions throw a
/// <see cref="FormatException"/>. The coordinate system of the table is not read: the coordinates are expected to be
/// longitudes and latitudes (EPSG:4326, the coordinate system of most files), and are not converted otherwise.
/// </para>
/// <para>
/// The identifier of a feature is the text of the primary key of the table, such as <c>fid</c>. The other columns are
/// the properties, with the type their declared type gives: a <see cref="bool"/> for <c>BOOLEAN</c>, an
/// <see cref="sbyte"/> for <c>TINYINT</c>, a <see cref="short"/> for <c>SMALLINT</c>, an <see cref="int"/> for
/// <c>MEDIUMINT</c>, a <see cref="long"/> for <c>INTEGER</c> and <c>INT</c>, a <see cref="float"/> for <c>FLOAT</c>, a
/// <see cref="double"/> for <c>DOUBLE</c> and <c>REAL</c>, a <see cref="string"/> for <c>TEXT</c>, a <see cref="DateOnly"/> for
/// <c>DATE</c> and a <see cref="DateTimeOffset"/> for <c>DATETIME</c> (the text when they cannot be parsed) and an array of bytes for
/// <c>BLOB</c>. A <c>NULL</c> value is not a property.
/// </para>
/// <para>Reading a file needs the native SQLite library of Microsoft.Data.Sqlite, with its R-tree module to read a box.</para>
/// </remarks>
public static class GeoPackage
{
    /// <summary>Gets the names of the feature tables of a GeoPackage file: the layers of the file, in the order of <c>gpkg_contents</c>.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The names of the tables.</returns>
    /// <exception cref="FormatException">The file is not a GeoPackage.</exception>
    public static IReadOnlyList<string> GetTables(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Execute(path, connection => GeoPackageReader.GetTables(connection));
    }

    /// <summary>Gets the names of the feature tables of an open GeoPackage database.</summary>
    /// <param name="connection">The open connection to the database.</param>
    /// <returns>The names of the tables.</returns>
    /// <exception cref="FormatException">The database is not a GeoPackage.</exception>
    public static IReadOnlyList<string> GetTables(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            return GeoPackageReader.GetTables(connection);
        }
        catch (SqliteException ex)
        {
            throw new FormatException("The GeoPackage cannot be read: " + ex.Message, ex);
        }
    }

    /// <summary>Reads the features of a table of a GeoPackage file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="tableName">The name of the feature table, or <see langword="null"/> when the file has a single one.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features, in the order of the primary key, in a collection named after the table.</returns>
    /// <exception cref="ArgumentException">The file has no feature table named <paramref name="tableName"/>.</exception>
    /// <exception cref="FormatException">The file is not a GeoPackage, has no feature table, or several ones and no <paramref name="tableName"/>, or a geometry is not valid.</exception>
    public static GeoFeatureCollection Read(string path, string? tableName = null, GeoPackageReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Execute(path, connection => ReadCore(connection, tableName, options));
    }

    /// <summary>Reads the features of a table of an open GeoPackage database.</summary>
    /// <param name="connection">The open connection to the database.</param>
    /// <param name="tableName">The name of the feature table, or <see langword="null"/> when the database has a single one.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features, in the order of the primary key, in a collection named after the table.</returns>
    /// <exception cref="ArgumentException">The database has no feature table named <paramref name="tableName"/>.</exception>
    /// <exception cref="FormatException">The database is not a GeoPackage, has no feature table, or several ones and no <paramref name="tableName"/>, or a geometry is not valid.</exception>
    public static GeoFeatureCollection Read(SqliteConnection connection, string? tableName = null, GeoPackageReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            return ReadCore(connection, tableName, options);
        }
        catch (SqliteException ex)
        {
            throw new FormatException("The GeoPackage cannot be read: " + ex.Message, ex);
        }
    }

    private static GeoFeatureCollection ReadCore(SqliteConnection connection, string? tableName, GeoPackageReaderOptions? options)
    {
        var table = GeoPackageReader.OpenTable(connection, tableName, options?.Bounds);
        using var command = table.Command;
        using var reader = command.ExecuteReader();
        var features = new GeoFeatureCollection { Name = table.Name };
        while (reader.Read())
        {
            if (table.ReadFeature(reader) is { } feature)
            {
                features.Add(feature);
            }
        }

        return features;
    }

    /// <summary>
    /// Reads the features of a table of a GeoPackage file one at a time, with bounded memory: the rows are read as the
    /// features are enumerated, and only the row being read is kept in memory.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="tableName">The name of the feature table, or <see langword="null"/> when the file has a single one.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the primary key. The file is opened when the enumeration starts, and closed when it ends.</returns>
    /// <exception cref="ArgumentException">The file has no feature table named <paramref name="tableName"/>.</exception>
    /// <exception cref="FormatException">The file is not a GeoPackage, has no feature table, or several ones and no <paramref name="tableName"/>, or a geometry is not valid. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(string path, string? tableName = null, GeoPackageReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileFeaturesAsync(path, tableName, options, cancellationToken);
    }

    /// <summary>
    /// Reads the features of a table of an open GeoPackage database one at a time, with bounded memory: the rows are read
    /// as the features are enumerated, and only the row being read is kept in memory.
    /// </summary>
    /// <param name="connection">The open connection to the database. It is used until the enumeration ends.</param>
    /// <param name="tableName">The name of the feature table, or <see langword="null"/> when the database has a single one.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, in the order of the primary key.</returns>
    /// <exception cref="ArgumentException">The database has no feature table named <paramref name="tableName"/>.</exception>
    /// <exception cref="FormatException">The database is not a GeoPackage, has no feature table, or several ones and no <paramref name="tableName"/>, or a geometry is not valid. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(SqliteConnection connection, string? tableName = null, GeoPackageReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return ReadConnectionFeaturesAsync(connection, tableName, options, cancellationToken);
    }

    private static async IAsyncEnumerable<GeoFeature> ReadFileFeaturesAsync(string path, string? tableName, GeoPackageReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(path, cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await foreach (var feature in ReadConnectionFeaturesAsync(connection, tableName, options, cancellationToken).ConfigureAwait(false))
            {
                yield return feature;
            }
        }
    }

    private static async IAsyncEnumerable<GeoFeature> ReadConnectionFeaturesAsync(SqliteConnection connection, string? tableName, GeoPackageReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        GeoPackageTable table;
        try
        {
            table = GeoPackageReader.OpenTable(connection, tableName, options?.Bounds);
        }
        catch (SqliteException ex)
        {
            throw new FormatException("The GeoPackage cannot be read: " + ex.Message, ex);
        }

        await using (table.Command.ConfigureAwait(false))
        {
            var reader = await ExecuteReaderAsync(table.Command, cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await ReadAsync(reader, cancellationToken).ConfigureAwait(false))
                {
                    if (table.ReadFeature(reader) is { } feature)
                    {
                        yield return feature;
                    }
                }
            }
        }
    }

    private static async Task<SqliteDataReader> ExecuteReaderAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            throw new FormatException("The GeoPackage cannot be read: " + ex.Message, ex);
        }
    }

    private static async Task<bool> ReadAsync(SqliteDataReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            throw new FormatException("The GeoPackage cannot be read: " + ex.Message, ex);
        }
    }

    private static T Execute<T>(string path, Func<SqliteConnection, T> action)
    {
        using var connection = Open(path);
        try
        {
            return action(connection);
        }
        catch (SqliteException ex)
        {
            throw new FormatException($"The file '{path}' is not a valid GeoPackage file: {ex.Message}", ex);
        }
    }

    // Without pooling, the file is closed with the connection, so that it can be replaced or deleted
    private static SqliteConnection Open(string path)
    {
        var connection = CreateConnection(path);
        try
        {
            connection.Open();
            return connection;
        }
        catch (SqliteException ex)
        {
            connection.Dispose();
            throw new FormatException($"The file '{path}' is not a valid GeoPackage file: {ex.Message}", ex);
        }
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connection = CreateConnection(path);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (SqliteException ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new FormatException($"The file '{path}' is not a valid GeoPackage file: {ex.Message}", ex);
        }
    }

    private static SqliteConnection CreateConnection(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"The file '{path}' does not exist.", path);

        return new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    }
}
