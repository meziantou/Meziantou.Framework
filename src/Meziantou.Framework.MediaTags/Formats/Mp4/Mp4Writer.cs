using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal sealed class Mp4Writer : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            // Read the entire file to get the atom structure
            var fileData = new byte[inputStream.Length];
            inputStream.ReadAtLeast(fileData, fileData.Length, throwOnEndOfStream: false);

            // Parse atoms
            inputStream.Position = 0;
            var atoms = Mp4Atom.ReadAtoms(inputStream, inputStream.Length);

            // Build new ilst data
            var ilstData = BuildIlstData(tags);

            // Find moov atom
            var moovAtom = atoms.FirstOrDefault(a => a.Type == "moov");
            if (moovAtom is null)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "No moov atom found.");

            // Compute the new file by copying atoms and replacing moov.udta.meta.ilst
            inputStream.Position = 0;
            WriteAtomsWithNewIlst(outputStream, atoms, ilstData, fileData);

            return MediaTagResult.Success();
        }
        catch (Exception ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    private static void WriteAtomsWithNewIlst(Stream output, List<Mp4Atom> atoms, byte[] ilstData, byte[] fileData)
    {
        foreach (var atom in atoms)
        {
            if (atom.Type == "moov")
            {
                // Rebuild moov with new ilst
                var newMoov = RebuildMoov(atom, ilstData, fileData);
                output.Write(newMoov);
            }
            else
            {
                // Copy atom as-is from file data
                output.Write(fileData, (int)atom.Position, (int)atom.Size);
            }
        }
    }

    private static byte[] RebuildMoov(Mp4Atom moovAtom, byte[] ilstData, byte[] fileData)
    {
        using var ms = new MemoryStream();

        // We need to rebuild the moov atom with the updated ilst
        // Strategy: copy all children except udta, then write new udta.meta.ilst
        var children = moovAtom.Children;

        foreach (var child in children)
        {
            if (child.Type == "udta")
            {
                // Build new udta.meta.ilst
                var newIlstAtom = BuildAtom("ilst", ilstData);
                var newMetaData = BuildMetaAtomData(newIlstAtom);
                var newMetaAtom = BuildAtom("meta", newMetaData);
                var newUdtaAtom = BuildAtom("udta", newMetaAtom);
                ms.Write(newUdtaAtom);
            }
            else
            {
                // Copy child atom as-is
                ms.Write(fileData, (int)child.Position, (int)child.Size);
            }
        }

        // If no udta existed, add one
        if (!children.Any(c => c.Type == "udta"))
        {
            var newIlstAtom = BuildAtom("ilst", ilstData);
            var newMetaData = BuildMetaAtomData(newIlstAtom);
            var newMetaAtom = BuildAtom("meta", newMetaData);
            var newUdtaAtom = BuildAtom("udta", newMetaAtom);
            ms.Write(newUdtaAtom);
        }

        // Wrap in moov atom
        return BuildAtom("moov", ms.ToArray());
    }

    private static byte[] BuildMetaAtomData(byte[] content)
    {
        // meta atom has 4 bytes version/flags before children
        var result = new byte[4 + content.Length];
        content.CopyTo(result, 4);
        return result;
    }

    private static byte[] BuildIlstData(MediaTagInfo tags)
    {
        using var ms = new MemoryStream();

        WriteTextAtom(ms, ItunesAtomNames.Title, tags.Title);
        WriteTextAtom(ms, ItunesAtomNames.Artist, tags.Artist);
        WriteTextAtom(ms, ItunesAtomNames.Album, tags.Album);
        WriteTextAtom(ms, ItunesAtomNames.AlbumArtist, tags.AlbumArtist);
        WriteTextAtom(ms, ItunesAtomNames.Genre, tags.Genre);

        if (tags.Year is not null)
            WriteTextAtom(ms, ItunesAtomNames.Year, tags.Year.Value.ToString("D4", CultureInfo.InvariantCulture));

        if (tags.TrackNumber is not null)
            WriteTrackDiskAtom(ms, ItunesAtomNames.TrackNumber, tags.TrackNumber.Value, tags.TrackTotal ?? 0);

        if (tags.DiscNumber is not null)
            WriteTrackDiskAtom(ms, ItunesAtomNames.DiscNumber, tags.DiscNumber.Value, tags.DiscTotal ?? 0);

        WriteTextAtom(ms, ItunesAtomNames.Composer, tags.Composer);
        WriteTextAtom(ms, ItunesAtomNames.Comment, tags.Comment);
        WriteTextAtom(ms, ItunesAtomNames.Copyright, tags.Copyright);

        if (tags.Bpm is not null)
            WriteUInt16Atom(ms, ItunesAtomNames.Bpm, (ushort)tags.Bpm.Value);

        if (tags.IsCompilation is not null)
            WriteByteAtom(ms, ItunesAtomNames.Compilation, (byte)(tags.IsCompilation.Value ? 1 : 0));

        foreach (var picture in tags.Pictures)
        {
            var typeIndicator = picture.MimeType == "image/png" ? 14u : 13u;
            WriteDataAtom(ms, ItunesAtomNames.CoverArt, typeIndicator, picture.Data);
        }

        return ms.ToArray();
    }

    private static void WriteTextAtom(MemoryStream ms, string atomType, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var valueBytes = Encoding.UTF8.GetBytes(value);
        WriteDataAtom(ms, atomType, 1, valueBytes); // type indicator 1 = UTF-8
    }

    private static void WriteTrackDiskAtom(MemoryStream ms, string atomType, int number, int total)
    {
        var data = new byte[8]; // 2 padding + 2 number + 2 total + 2 padding
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)number);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)total);
        WriteDataAtom(ms, atomType, 0, data); // type indicator 0 = implicit
    }

    private static void WriteUInt16Atom(MemoryStream ms, string atomType, ushort value)
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, value);
        WriteDataAtom(ms, atomType, 21, data); // type indicator 21 = signed integer
    }

    private static void WriteByteAtom(MemoryStream ms, string atomType, byte value)
    {
        WriteDataAtom(ms, atomType, 21, [value]); // type indicator 21 = signed integer
    }

    private static void WriteDataAtom(MemoryStream ms, string atomType, uint typeIndicator, byte[] value)
    {
        // data atom: size(4) + "data"(4) + type(4) + locale(4) + value
        var dataAtomSize = 8 + 4 + 4 + value.Length;
        var dataAtom = new byte[dataAtomSize];
        BinaryPrimitives.WriteUInt32BigEndian(dataAtom, (uint)dataAtomSize);
        Encoding.Latin1.GetBytes("data", dataAtom.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(dataAtom.AsSpan(8), typeIndicator);
        // locale at 12-15 is 0
        value.CopyTo(dataAtom, 16);

        // Wrapping ilst item atom: size(4) + type(4) + data atom
        var itemSize = 8 + dataAtomSize;
        Span<byte> itemHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(itemHeader, (uint)itemSize);
        Encoding.Latin1.GetBytes(atomType, itemHeader[4..]);
        ms.Write(itemHeader);
        ms.Write(dataAtom);
    }

    private static byte[] BuildAtom(string type, byte[] data)
    {
        var result = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        Encoding.Latin1.GetBytes(type, result.AsSpan(4));
        data.CopyTo(result, 8);
        return result;
    }
}
