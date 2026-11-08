using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>
/// Reads and writes ESRI shapefiles (ESRI Shapefile Technical Description, July 1998): the geometries of the
/// <c>.shp</c> file with the attributes of the dBASE <c>.dbf</c> file, from files, from streams, or from a <c>.zip</c> archive.
/// </summary>
/// <remarks>
/// <para>
/// The reader reads every shape type. The Z values become the altitudes; the M values (measures) are dropped. A PolyLine
/// of one part is a <see cref="GeoLineString"/>, and of several parts a <see cref="GeoMultiLineString"/>; a MultiPoint
/// is always a <see cref="GeoMultiPoint"/>. The rings of a Polygon are grouped as the shapefile orients them: each
/// clockwise ring is an exterior ring, and each counterclockwise ring is a hole of the smallest exterior ring that contains
/// it, or a polygon when no exterior ring contains it; when no ring is clockwise, a ring is a hole when it is inside an odd
/// number of rings. The positions are kept in the order of the file, so the exterior rings are clockwise. A MultiPatch
/// becomes polygons, as GDAL reads it: one per triangle of its strips and fans, and one per outer or first ring, with the
/// inner rings and the rings that follow it as its holes. A null shape, a shape without positions and a point of NaN
/// coordinates give a feature without geometry. The <c>.shx</c> and <c>.prj</c> files are not read: the coordinates
/// are expected to be longitudes and latitudes, and are not converted when the <c>.prj</c> file declares another
/// coordinate system.
/// </para>
/// <para>
/// The features are the records of the <c>.dbf</c> file, as GDAL reads them: a record marked as deleted is skipped with its
/// shape, a record without shape has no geometry, and the shapes after the last record are ignored. Without a <c>.dbf</c>
/// file, the features are the shapes, without properties. The property values are a <see cref="string"/> for a character
/// field (without its trailing spaces), a <see cref="double"/> for a numeric field (N or F), a <see cref="bool"/> for a
/// logical field, a <see cref="DateOnly"/> for a date field, and the text of the field for the other types, such as the
/// block number of a memo field. A blank value, an empty text, a number of asterisks, a <c>?</c> logical value and an
/// invalid date are <see langword="null"/>. When two fields have the same name, the second one is named <c>name_1</c>.
/// The features have no identifier.
/// </para>
/// <para>
/// The text of the <c>.dbf</c> file is decoded with <see cref="ShapefileReaderOptions.Encoding"/>, else the code page of the
/// <c>.cpg</c> file (such as <c>UTF-8</c>, <c>ISO-8859-1</c> or <c>1252</c>), else the language driver ID of the
/// <c>.dbf</c> header. UTF-8, ISO-8859-1 and Windows-1252 are always available; the other code pages (such as 437, 850 or
/// 1251) are used when the application registered a provider, such as <c>CodePagesEncodingProvider</c>. When no encoding
/// is declared, or the declared one is not available, each value is decoded as UTF-8 when it is valid UTF-8, else as ISO-8859-1.
/// </para>
/// <para>
/// The writer writes the <c>.shp</c>, <c>.shx</c>, <c>.dbf</c>, <c>.cpg</c> (UTF-8) and <c>.prj</c> (WGS 84 longitudes and
/// latitudes) files. A shapefile has one shape type: Point for <see cref="GeoPoint"/> features, MultiPoint when a feature
/// is a <see cref="GeoMultiPoint"/>, PolyLine for lines and Polygon for polygons, with Z (PointZ, MultiPointZ, PolyLineZ,
/// PolygonZ) when a position has an altitude, the positions without altitude getting 0. Features that mix points, lines
/// and polygons, or that have a <see cref="GeoGeometryCollection"/>, throw an <see cref="ArgumentException"/>. A feature
/// without geometry, or with an empty geometry, is a null shape. The exterior rings are written clockwise and the holes
/// counterclockwise, closed.
/// </para>
/// <para>
/// The fields are the union of the property names, in the order they are first seen. The names are cut to 10 bytes of
/// UTF-8, and a name equal to a previous one, ignoring the case, gets a suffix: <c>name_1</c>, <c>name_2</c>... as GDAL
/// does. A field gets its type from its non-null values: a string is a character field as wide as the longest value, up
/// to 254 bytes (a longer value is cut, between two characters); integers, and whole numbers below 10<sup>15</sup>, are an
/// N field without decimals; other numbers are an N field of width 24 and 15 decimals, as GDAL writes them, holding the
/// shortest text that gives the number back; a <see cref="bool"/> is a logical field; a <see cref="DateOnly"/>, a
/// <see cref="DateTime"/> or a <see cref="DateTimeOffset"/> is a date field, without the time of day. A field whose values
/// have different types, or have another type, is a character field of their text. A <see langword="null"/> or non-finite
/// number is written as a null value. When no feature has a property, the file gets an <c>FID</c> field with the index
/// of the feature, as GDAL does. The identifiers of the features are not written.
/// </para>
/// </remarks>
public static class Shapefile
{
    // The WGS 84 geographic coordinate system (EPSG:4326), as GDAL writes it in a .prj file
    private const string Wgs84Projection = """GEOGCS["GCS_WGS_1984",DATUM["D_WGS_1984",SPHEROID["WGS_1984",6378137.0,298.257223563]],PRIMEM["Greenwich",0.0],UNIT["Degree",0.0174532925199433]]""";

    private const int MaxCodePageFileLength = 1024;

    /// <summary>Reads a shapefile: the <c>.shp</c> file and, when they exist, the <c>.dbf</c> and <c>.cpg</c> files of the same name, whatever the case of their extension.</summary>
    /// <param name="shpPath">The path of the <c>.shp</c> file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features, in a collection named after the file, without its extension.</returns>
    /// <exception cref="FormatException">A file is not valid.</exception>
    public static GeoFeatureCollection Read(string shpPath, ShapefileReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(shpPath);
        var fullPath = Path.GetFullPath(shpPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        using var shp = File.OpenRead(fullPath);
        var dbfPath = FindSibling(directory, name, ".dbf");
        using var dbf = dbfPath is null ? null : File.OpenRead(dbfPath);
        string? codePage = null;
        if (FindSibling(directory, name, ".cpg") is { } cpgPath)
        {
            using var cpg = File.OpenRead(cpgPath);
            codePage = ReadCodePage(cpg);
        }

        var features = ReadCore(shp, dbf, codePage, options);
        features.Name = name;
        return features;
    }

    /// <summary>Reads a shapefile from streams.</summary>
    /// <param name="shp">The content of the <c>.shp</c> file. It is left open.</param>
    /// <param name="dbf">The content of the <c>.dbf</c> file, or <see langword="null"/> to read the geometries only. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults. Without a <c>.cpg</c> file, the encoding comes from the options or from the <c>.dbf</c> header.</param>
    /// <returns>The features.</returns>
    /// <exception cref="FormatException">A file is not valid.</exception>
    public static GeoFeatureCollection Read(Stream shp, Stream? dbf, ShapefileReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(shp);
        return ReadCore(shp, dbf, codePage: null, options);
    }

    /// <summary>Reads the shapefile of a <c>.zip</c> archive, such as one downloaded from an open data portal.</summary>
    /// <param name="zip">The archive. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features, in a collection named after the <c>.shp</c> file, without its extension.</returns>
    /// <exception cref="FormatException">The archive is not valid, has no <c>.shp</c> file or several ones, or a file is not valid.</exception>
    public static GeoFeatureCollection ReadZip(Stream zip, ShapefileReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        return ReadZipCore(zip, layerName: null, options);
    }

    /// <summary>Reads a shapefile of a <c>.zip</c> archive that has several ones.</summary>
    /// <param name="zip">The archive. It is left open.</param>
    /// <param name="layerName">The name of the <c>.shp</c> file, without its extension, ignoring the case, such as <c>roads</c> or <c>data/roads</c>.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The features, in a collection named after the <c>.shp</c> file, without its extension.</returns>
    /// <exception cref="ArgumentException">The archive has no <c>.shp</c> file of this name.</exception>
    /// <exception cref="FormatException">The archive is not valid, or a file is not valid.</exception>
    public static GeoFeatureCollection ReadZip(Stream zip, string layerName, ShapefileReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrEmpty(layerName);
        return ReadZipCore(zip, layerName, options);
    }

    /// <summary>
    /// Reads the features of a shapefile one at a time, with bounded memory: the <c>.shp</c> file and, when they exist,
    /// the <c>.dbf</c> and <c>.cpg</c> files of the same name, whatever the case of their extension. The records are read
    /// as the features are enumerated, and only the record being read is kept in memory.
    /// </summary>
    /// <param name="shpPath">The path of the <c>.shp</c> file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, as <see cref="Read(string, ShapefileReaderOptions?)"/> reads them. The files are opened when the enumeration starts, and closed when it ends.</returns>
    /// <exception cref="FormatException">A file is not valid. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(string shpPath, ShapefileReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(shpPath);
        return ReadFeaturesFromFilesAsync(shpPath, options, cancellationToken);
    }

    /// <summary>Reads the features of a shapefile from streams, one at a time, with bounded memory.</summary>
    /// <param name="shp">The content of the <c>.shp</c> file. It is read as the features are enumerated, and left open.</param>
    /// <param name="dbf">The content of the <c>.dbf</c> file, or <see langword="null"/> to read the geometries only. It is read as the features are enumerated, and left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults. Without a <c>.cpg</c> file, the encoding comes from the options or from the <c>.dbf</c> header.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, as <see cref="Read(Stream, Stream?, ShapefileReaderOptions?)"/> reads them.</returns>
    /// <exception cref="FormatException">A file is not valid. The features read before the error are returned first.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadFeaturesAsync(Stream shp, Stream? dbf, ShapefileReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shp);
        return ReadFeaturesCoreAsync(shp, dbf, codePage: null, options, cancellationToken);
    }

    /// <summary>
    /// Reads the features of the shapefile of a <c>.zip</c> archive one at a time, with bounded memory: the files are
    /// decompressed as the features are enumerated.
    /// </summary>
    /// <param name="zip">The archive. It must be seekable, to read the directory at its end. It is left open.</param>
    /// <param name="layerName">The name of the <c>.shp</c> file, without its extension, ignoring the case, such as <c>roads</c> or <c>data/roads</c>, or <see langword="null"/> when the archive has one shapefile.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to cancel the reading.</param>
    /// <returns>The features, as <see cref="ReadZip(Stream, ShapefileReaderOptions?)"/> reads them.</returns>
    /// <exception cref="ArgumentException">The archive has no <c>.shp</c> file named <paramref name="layerName"/>.</exception>
    /// <exception cref="FormatException">The archive is not valid, has no <c>.shp</c> file, or several ones and no <paramref name="layerName"/>, or a file is not valid.</exception>
    public static IAsyncEnumerable<GeoFeature> ReadZipFeaturesAsync(Stream zip, string? layerName = null, ShapefileReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zip);
        if (layerName is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(layerName);
        }

        return ReadZipFeaturesCoreAsync(zip, layerName, options, cancellationToken);
    }

    /// <summary>Writes features as a shapefile: the <c>.shp</c>, <c>.shx</c>, <c>.dbf</c>, <c>.cpg</c> and <c>.prj</c> files, replacing the existing ones.</summary>
    /// <param name="features">The features.</param>
    /// <param name="shpPath">The path of the <c>.shp</c> file. The other files get the same name, with their extension.</param>
    /// <exception cref="ArgumentException">A position is not finite, the features mix points, lines and polygons, or have a <see cref="GeoGeometryCollection"/>, or the files would be too large.</exception>
    public static void Write(GeoFeatureCollection features, string shpPath)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentException.ThrowIfNullOrEmpty(shpPath);
        var (shapes, attributes) = Prepare(features);
        var basePath = shpPath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase) ? shpPath[..^4] : shpPath;
        using (var shp = File.Create(basePath + ".shp"))
        {
            shapes.WriteShp(shp);
        }

        using (var shx = File.Create(basePath + ".shx"))
        {
            shapes.WriteShx(shx);
        }

        using (var dbf = File.Create(basePath + ".dbf"))
        {
            attributes.Write(dbf);
        }

        File.WriteAllText(basePath + ".cpg", "UTF-8", Encoding.ASCII);
        File.WriteAllText(basePath + ".prj", Wgs84Projection, Encoding.ASCII);
    }

    /// <summary>Writes features as the <c>.shp</c>, <c>.shx</c> and <c>.dbf</c> files of a shapefile. The text of the <c>.dbf</c> file is encoded in UTF-8: write <c>UTF-8</c> in a <c>.cpg</c> file next to them.</summary>
    /// <param name="features">The features.</param>
    /// <param name="shp">The stream to write the <c>.shp</c> file to. It is left open.</param>
    /// <param name="shx">The stream to write the <c>.shx</c> file to. It is left open.</param>
    /// <param name="dbf">The stream to write the <c>.dbf</c> file to. It is left open.</param>
    /// <exception cref="ArgumentException">A position is not finite, the features mix points, lines and polygons, or have a <see cref="GeoGeometryCollection"/>, or the files would be too large.</exception>
    public static void Write(GeoFeatureCollection features, Stream shp, Stream shx, Stream dbf)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(shp);
        ArgumentNullException.ThrowIfNull(shx);
        ArgumentNullException.ThrowIfNull(dbf);
        var (shapes, attributes) = Prepare(features);
        shapes.WriteShp(shp);
        shapes.WriteShx(shx);
        attributes.Write(dbf);
    }

    /// <summary>Writes features as a shapefile in a <c>.zip</c> archive: the <c>.shp</c>, <c>.shx</c>, <c>.dbf</c>, <c>.cpg</c> and <c>.prj</c> files.</summary>
    /// <param name="features">The features.</param>
    /// <param name="zip">The stream to write the archive to. It is left open.</param>
    /// <param name="name">The name of the files in the archive, without extension, such as <c>roads</c>.</param>
    /// <exception cref="ArgumentException">A position is not finite, the features mix points, lines and polygons, or have a <see cref="GeoGeometryCollection"/>, or the files would be too large.</exception>
    public static void WriteZip(GeoFeatureCollection features, Stream zip, string name)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var (shapes, attributes) = Prepare(features);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, name + ".shp", shapes.WriteShp);
        WriteEntry(archive, name + ".shx", shapes.WriteShx);
        WriteEntry(archive, name + ".dbf", attributes.Write);
        WriteEntry(archive, name + ".cpg", stream => stream.Write("UTF-8"u8));
        WriteEntry(archive, name + ".prj", stream => stream.Write(Encoding.ASCII.GetBytes(Wgs84Projection)));
    }

    private static (ShapefileShapeWriter Shapes, DbaseWriter Attributes) Prepare(GeoFeatureCollection features)
    {
        GeoValidation.EnsureFinite(features, nameof(features));
        return (ShapefileShapeWriter.Create(features, nameof(features)), DbaseWriter.Create(features, nameof(features)));
    }

    private static void WriteEntry(ZipArchive archive, string name, Action<Stream> write)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        write(stream);
    }

    private static GeoFeatureCollection ReadCore(Stream shp, Stream? dbf, string? codePage, ShapefileReaderOptions? options)
    {
        var shapes = new ShapefileShapeReader(shp);
        var features = new GeoFeatureCollection();
        if (dbf is null)
        {
            while (shapes.TryRead(out var geometry))
            {
                features.Add(new GeoFeature(geometry));
            }

            return features;
        }

        var attributes = DbaseReader.Create(dbf, options?.Encoding, codePage);
        var hasShapes = true;
        for (var i = 0; i < attributes.RecordCount; i++)
        {
            var properties = attributes.Read(out var deleted);
            GeoGeometry? geometry = null;
            hasShapes = hasShapes && shapes.TryRead(out geometry);
            if (!deleted)
            {
                features.Add(new GeoFeature(geometry, properties));
            }
        }

        return features;
    }

    private static async IAsyncEnumerable<GeoFeature> ReadFeaturesFromFilesAsync(string shpPath, ShapefileReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(shpPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        string? codePage = null;
        if (FindSibling(directory, name, ".cpg") is { } cpgPath)
        {
            var cpg = File.OpenRead(cpgPath);
            await using (cpg.ConfigureAwait(false))
            {
                codePage = await ReadCodePageAsync(cpg, cancellationToken).ConfigureAwait(false);
            }
        }

        var dbfPath = FindSibling(directory, name, ".dbf");
        var shp = OpenAsyncRead(fullPath);
        await using (shp.ConfigureAwait(false))
        {
            // Stream.Null stands for a missing .dbf file, so that both streams are disposed the same way
            var dbf = dbfPath is null ? Stream.Null : OpenAsyncRead(dbfPath);
            await using (dbf.ConfigureAwait(false))
            {
                await foreach (var feature in ReadFeaturesCoreAsync(shp, dbfPath is null ? null : dbf, codePage, options, cancellationToken).ConfigureAwait(false))
                {
                    yield return feature;
                }
            }
        }

        static FileStream OpenAsyncRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static async IAsyncEnumerable<GeoFeature> ReadFeaturesCoreAsync(Stream shp, Stream? dbf, string? codePage, ShapefileReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var shapes = await ShapefileShapeReader.CreateAsync(shp, cancellationToken).ConfigureAwait(false);
        if (dbf is null)
        {
            while (await shapes.TryReadAsync(cancellationToken).ConfigureAwait(false) is (true, var geometry))
            {
                yield return new GeoFeature(geometry);
            }

            yield break;
        }

        var attributes = await DbaseReader.CreateAsync(dbf, options?.Encoding, codePage, cancellationToken).ConfigureAwait(false);
        var hasShapes = true;
        for (var i = 0; i < attributes.RecordCount; i++)
        {
            var (properties, deleted) = await attributes.ReadAsync(cancellationToken).ConfigureAwait(false);
            GeoGeometry? geometry = null;
            if (hasShapes)
            {
                (hasShapes, geometry) = await shapes.TryReadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!deleted)
            {
                yield return new GeoFeature(geometry, properties);
            }
        }
    }

    private static async IAsyncEnumerable<GeoFeature> ReadZipFeaturesCoreAsync(Stream zip, string? layerName, ShapefileReaderOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ZipArchive? archive = null;
        Stream? shp = null;
        Stream? dbf = null;
        IAsyncEnumerator<GeoFeature>? features = null;
        try
        {
            try
            {
                archive = await ZipArchive.CreateAsync(zip, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken).ConfigureAwait(false);
                var (shpEntry, dbfEntry, cpgEntry) = FindEntries(archive, layerName);
                string? codePage = null;
                if (cpgEntry is not null)
                {
                    var cpg = await cpgEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using (cpg.ConfigureAwait(false))
                    {
                        codePage = await ReadCodePageAsync(cpg, cancellationToken).ConfigureAwait(false);
                    }
                }

                shp = await shpEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (dbfEntry is not null)
                {
                    dbf = await dbfEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                features = ReadFeaturesCoreAsync(shp, dbf, codePage, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                throw ToZipFormatException(ex);
            }

            while (true)
            {
                try
                {
                    if (!await features.MoveNextAsync().ConfigureAwait(false))
                        break;
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                {
                    throw ToZipFormatException(ex);
                }

                yield return features.Current;
            }
        }
        finally
        {
            if (features is not null)
            {
                await features.DisposeAsync().ConfigureAwait(false);
            }

            if (dbf is not null)
            {
                await dbf.DisposeAsync().ConfigureAwait(false);
            }

            if (shp is not null)
            {
                await shp.DisposeAsync().ConfigureAwait(false);
            }

            if (archive is not null)
            {
                await archive.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static FormatException ToZipFormatException(Exception exception) => exception is NotSupportedException
        ? new FormatException("The zip archive has an entry that cannot be read, such as an encrypted entry: " + exception.Message, exception)
        : new FormatException("The archive is not a valid zip file: " + exception.Message, exception);

    private static GeoFeatureCollection ReadZipCore(Stream zip, string? layerName, ShapefileReaderOptions? options)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);

            var (shpEntry, dbfEntry, cpgEntry) = FindEntries(archive, layerName);
            string? codePage = null;
            if (cpgEntry is not null)
            {
                using var cpg = cpgEntry.Open();
                codePage = ReadCodePage(cpg);
            }

            using var shp = shpEntry.Open();
            using var dbf = dbfEntry?.Open();
            var features = ReadCore(shp, dbf, codePage, options);
            features.Name = shpEntry.Name[..^4];
            return features;
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("The archive is not a valid zip file: " + ex.Message, ex);
        }
        catch (NotSupportedException ex)
        {
            throw new FormatException("The zip archive has an entry that cannot be read, such as an encrypted entry: " + ex.Message, ex);
        }
    }

    private static (ZipArchiveEntry Shp, ZipArchiveEntry? Dbf, ZipArchiveEntry? Cpg) FindEntries(ZipArchive archive, string? layerName)
    {
        // The metadata of the macOS archiver (__MACOSX/, ._name) are not shapefiles
        var shpEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".shp", StringComparison.OrdinalIgnoreCase) && !entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal) && !entry.Name.StartsWith("._", StringComparison.Ordinal))
            .ToList();
        if (shpEntries.Count is 0)
            throw new FormatException("The archive has no .shp file.");

        ZipArchiveEntry shpEntry;
        if (layerName is null)
        {
            if (shpEntries.Count > 1)
                throw new FormatException($"The archive has several shapefiles ({string.Join(", ", shpEntries.Select(entry => entry.FullName))}): give the name of the one to read.");

            shpEntry = shpEntries[0];
        }
        else
        {
            shpEntry = shpEntries.Find(entry => string.Equals(entry.FullName[..^4], layerName, StringComparison.OrdinalIgnoreCase))
                ?? shpEntries.Find(entry => string.Equals(entry.Name[..^4], layerName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"The archive has no shapefile named '{layerName}'; it has: {string.Join(", ", shpEntries.Select(entry => entry.FullName))}.", nameof(layerName));
        }

        var basePath = shpEntry.FullName[..^4];
        return (shpEntry, FindEntry(archive, basePath + ".dbf"), FindEntry(archive, basePath + ".cpg"));
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string fullName) =>
        archive.GetEntry(fullName) ?? archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, fullName, StringComparison.OrdinalIgnoreCase));

    // The files of a shapefile often come from Windows, with upper-case extensions such as .DBF
    private static string? FindSibling(string directory, string name, string extension)
    {
        var path = Path.Combine(directory, name + extension);
        if (File.Exists(path))
            return path;

        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = false };
        return Directory.EnumerateFiles(directory, name + ".*", options).FirstOrDefault(file => string.Equals(Path.GetFileName(file), name + extension, StringComparison.OrdinalIgnoreCase));
    }

    private static async ValueTask<string> ReadCodePageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxCodePageFileLength];
        var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(buffer, 0, length).TrimStart('\uFEFF').Trim();
    }

    private static string ReadCodePage(Stream stream)
    {
        var buffer = new byte[MaxCodePageFileLength];
        var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Encoding.UTF8.GetString(buffer, 0, length).TrimStart('\uFEFF').Trim();
    }
}
