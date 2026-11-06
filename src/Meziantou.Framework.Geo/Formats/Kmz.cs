using System.IO.Compression;

namespace Meziantou.Framework.Geo;

/// <summary>Reads and writes KMZ: a KML document, <see cref="Kml"/>, in a zip archive, with the files it references.</summary>
/// <remarks>
/// The reader reads the <c>doc.kml</c> entry, or else the first <c>.kml</c> entry at the root of the archive, or else
/// the first <c>.kml</c> entry of the archive, and ignores the other files, such as the icons and the images of the
/// overlays. The writer writes the KML document in a <c>doc.kml</c> entry.
/// </remarks>
public static class Kmz
{
    private const string DocumentEntryName = "doc.kml";

    /// <summary>Reads the placemarks of a KMZ archive.</summary>
    /// <param name="stream">The KMZ archive. It is left open. When it cannot seek, it is copied in memory.</param>
    /// <returns>The placemarks of the KML document of the archive.</returns>
    /// <exception cref="FormatException">The stream is not a zip archive, the archive has no KML document, or the document is not well-formed XML or not KML.</exception>
    public static GeoFeatureCollection Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = FindDocument(archive) ?? throw new FormatException("The KMZ archive has no KML document.");
            using var entryStream = entry.Open();
            return Kml.Read(entryStream);
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("The document is not a valid KMZ archive: " + ex.Message, ex);
        }
        catch (NotSupportedException ex)
        {
            throw new FormatException("The KMZ archive has an entry that cannot be read, such as an encrypted entry: " + ex.Message, ex);
        }
    }

    /// <summary>Writes features as a KMZ archive, with a <c>doc.kml</c> entry.</summary>
    /// <param name="features">The features.</param>
    /// <param name="stream">The stream to write to. It is left open.</param>
    /// <param name="options">The options of the KML document, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A position is not finite, or a text contains a character that XML cannot represent.</exception>
    public static void Write(GeoFeatureCollection features, Stream stream, KmlWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(stream);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var entry = archive.CreateEntry(DocumentEntryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        Kml.Write(features, entryStream, options);
    }

    private static ZipArchiveEntry? FindDocument(ZipArchive archive)
    {
        var documents = archive.Entries.Where(entry => entry.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)).ToList();
        return documents.Find(entry => string.Equals(entry.FullName, DocumentEntryName, StringComparison.OrdinalIgnoreCase))
            ?? documents.Find(entry => entry.FullName.IndexOfAny(['/', '\\']) < 0)
            ?? documents.FirstOrDefault();
    }
}
