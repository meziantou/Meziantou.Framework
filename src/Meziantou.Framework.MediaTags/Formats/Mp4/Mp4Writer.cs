using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal sealed class Mp4Writer : IMediaTagWriter
{
    private const string ItunesMean = "com.apple.iTunes";

    /// <summary>The largest moov atom this writer reads into memory.</summary>
    /// <remarks>Only moov is materialized; the audio is streamed, so a file of any size can be tagged.</remarks>
    private const long MaxMoovSize = 64L * 1024 * 1024;

    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            inputStream.Position = 0;

            var atoms = Mp4Atom.ReadAtoms(inputStream, inputStream.Length, out var complete);

            // Rebuilding the file from a partial parse silently drops every atom after the bad one, mdat
            // included, and this output is about to replace the caller's file.
            if (!complete)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The MP4 atoms do not cover the whole file.");

            var moovAtom = atoms.Find(a => a.Type == "moov");
            if (moovAtom is null)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "No moov atom found.");

            if (moovAtom.Size > MaxMoovSize)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The moov atom is too large to rewrite.");

            inputStream.Position = moovAtom.Position;
            var moovData = new byte[moovAtom.Size];
            if (inputStream.ReadAtLeast(moovData, moovData.Length, throwOnEndOfStream: false) < moovData.Length)
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended inside the moov atom.");

            if (!TryBuildIlstData(tags, moovAtom, moovData, out var ilstData, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            var newMoov = RebuildMoov(moovAtom, moovData, ilstData);

            // Resizing moov moves every atom after it. Sample chunk offsets are absolute file offsets, so
            // leaving them alone makes the audio of any file whose moov precedes its mdat — everything
            // produced with faststart — decode from the wrong place.
            var delta = newMoov.Length - moovAtom.Size;
            if (delta != 0 && !TryAdjustChunkOffsets(newMoov, moovAtom.Position + moovAtom.Size, delta, out var offsetError))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, offsetError);

            foreach (var atom in atoms)
            {
                if (ReferenceEquals(atom, moovAtom))
                {
                    outputStream.Write(newMoov);
                }
                else if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, atom.Position, atom.Size))
                {
                    return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended inside an MP4 atom.");
                }
            }

            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }

    /// <summary>
    /// Shifts every chunk offset that points at data which moved when moov was resized.
    /// </summary>
    private static bool TryAdjustChunkOffsets(byte[] moovData, long movedFrom, long delta, out string? error)
    {
        error = null;

        using var stream = new MemoryStream(moovData, writable: false);
        var atoms = Mp4Atom.ReadAtoms(stream, moovData.Length, out var complete);
        if (!complete)
        {
            error = "The rebuilt moov atom could not be parsed.";
            return false;
        }

        foreach (var root in atoms)
        {
            foreach (var atom in root.DescendantsAndSelf())
            {
                var entrySize = atom.Type switch
                {
                    Mp4Atom.ChunkOffsetTable => 4,
                    Mp4Atom.ChunkOffsetTable64 => 8,
                    _ => 0,
                };

                if (entrySize != 0 && !TryAdjustChunkOffsetTable(atom, moovData, movedFrom, delta, entrySize, out error))
                    return false;
            }
        }

        return true;
    }

    private static bool TryAdjustChunkOffsetTable(Mp4Atom atom, byte[] moovData, long movedFrom, long delta, int entrySize, out string? error)
    {
        error = null;

        var payloadStart = (int)(atom.Position + atom.HeaderSize);
        var payloadLength = (int)(atom.Size - atom.HeaderSize);

        // version/flags(4) + entry count(4)
        if (payloadLength < 8)
        {
            error = "A chunk offset table is truncated.";
            return false;
        }

        var payload = moovData.AsSpan(payloadStart, payloadLength);
        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        if ((long)entryCount * entrySize > payloadLength - 8)
        {
            error = "A chunk offset table declares more entries than it contains.";
            return false;
        }

        var entries = payload[8..];
        for (var i = 0; i < entryCount; i++)
        {
            var slot = entries.Slice(i * entrySize, entrySize);
            var value = entrySize == 4
                ? BinaryPrimitives.ReadUInt32BigEndian(slot)
                : (long)BinaryPrimitives.ReadUInt64BigEndian(slot);

            // Data before the old end of moov did not move.
            if (value < movedFrom)
                continue;

            var adjusted = value + delta;
            if (adjusted < 0)
            {
                error = "A chunk offset would become negative.";
                return false;
            }

            if (entrySize == 4)
            {
                if (adjusted > uint.MaxValue)
                {
                    error = "A chunk offset no longer fits in a 32-bit chunk offset table.";
                    return false;
                }

                BinaryPrimitives.WriteUInt32BigEndian(slot, (uint)adjusted);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(slot, (ulong)adjusted);
            }
        }

        return true;
    }

    private static byte[] RebuildMoov(Mp4Atom moovAtom, byte[] moovData, byte[] ilstData)
    {
        using var ms = new MemoryStream();
        var udtaWritten = false;

        foreach (var child in moovAtom.Children)
        {
            if (child.Type == "udta" && !udtaWritten)
            {
                ms.Write(BuildUdta(child, moovAtom, moovData, ilstData));
                udtaWritten = true;
            }
            else
            {
                ms.Write(GetAtomBytes(child, moovAtom, moovData));
            }
        }

        if (!udtaWritten)
            ms.Write(BuildUdta(existingUdta: null, moovAtom, moovData, ilstData));

        return BuildAtom("moov", ms.ToArray());
    }

    private static byte[] BuildUdta(Mp4Atom? existingUdta, Mp4Atom moovAtom, byte[] moovData, byte[] ilstData)
    {
        using var ms = new MemoryStream();
        var metaWritten = false;

        if (existingUdta is not null)
        {
            foreach (var child in existingUdta.Children)
            {
                if (child.Type == "meta" && !metaWritten)
                {
                    ms.Write(BuildMeta(child, moovAtom, moovData, ilstData));
                    metaWritten = true;
                }
                else
                {
                    // Anything else under udta — an m4b chapter list, Windows metadata — belongs to the user
                    // and is copied through rather than dropped.
                    ms.Write(GetAtomBytes(child, moovAtom, moovData));
                }
            }
        }

        if (!metaWritten)
            ms.Write(BuildMeta(existingMeta: null, moovAtom, moovData, ilstData));

        return BuildAtom("udta", ms.ToArray());
    }

    private static byte[] BuildMeta(Mp4Atom? existingMeta, Mp4Atom moovAtom, byte[] moovData, byte[] ilstData)
    {
        using var ms = new MemoryStream();

        // meta atom has 4 bytes version/flags, then a handler box, then the children. The handler box
        // declares the metadata as iTunes-style: readers such as iTunes and ffmpeg use it to recognise
        // the ilst box, and without it they report the file as having no tags at all.
        Span<byte> versionAndFlags = stackalloc byte[4];
        ms.Write(versionAndFlags);
        ms.Write(BuildHandlerAtom());

        var ilstWritten = false;
        if (existingMeta is not null)
        {
            foreach (var child in existingMeta.Children)
            {
                if (child.Type == "ilst" && !ilstWritten)
                {
                    ms.Write(BuildAtom("ilst", ilstData));
                    ilstWritten = true;
                }
                else if (child.Type != "hdlr")
                {
                    ms.Write(GetAtomBytes(child, moovAtom, moovData));
                }
            }
        }

        if (!ilstWritten)
            ms.Write(BuildAtom("ilst", ilstData));

        return BuildAtom("meta", ms.ToArray());
    }

    private static ReadOnlySpan<byte> GetAtomBytes(Mp4Atom atom, Mp4Atom moovAtom, byte[] moovData)
        => moovData.AsSpan((int)(atom.Position - moovAtom.Position), (int)atom.Size);

    private static byte[] BuildHandlerAtom()
    {
        // hdlr payload: version/flags(4) + predefined(4) + handler type(4) + reserved(12) + empty name(1)
        var data = new byte[25];
        Encoding.Latin1.GetBytes("mdir", data.AsSpan(8, 4));
        Encoding.Latin1.GetBytes("appl", data.AsSpan(12, 4));
        return BuildAtom("hdlr", data);
    }

    private static bool TryBuildIlstData(MediaTagInfo tags, Mp4Atom moovAtom, byte[] moovData, out byte[] ilstData, out string? error)
    {
        ilstData = [];

        if (!TryValidateRanges(tags, out error))
            return false;

        using var ms = new MemoryStream();

        WriteTextAtom(ms, ItunesAtomNames.Title, tags.Title);
        WriteTextAtom(ms, ItunesAtomNames.Artist, tags.Artist);
        WriteTextAtom(ms, ItunesAtomNames.Album, tags.Album);
        WriteTextAtom(ms, ItunesAtomNames.AlbumArtist, tags.AlbumArtist);
        WriteTextAtom(ms, ItunesAtomNames.Genre, tags.Genre);

        if (tags.Year is not null)
            WriteTextAtom(ms, ItunesAtomNames.Year, tags.Year.Value.ToString("D4", CultureInfo.InvariantCulture));

        // The number and the total share one atom, so writing it only when the number is set would discard a
        // total that was supplied on its own.
        if (tags.TrackNumber is not null || tags.TrackTotal is not null)
            WriteTrackDiskAtom(ms, ItunesAtomNames.TrackNumber, tags.TrackNumber ?? 0, tags.TrackTotal ?? 0);

        if (tags.DiscNumber is not null || tags.DiscTotal is not null)
            WriteTrackDiskAtom(ms, ItunesAtomNames.DiscNumber, tags.DiscNumber ?? 0, tags.DiscTotal ?? 0);

        WriteTextAtom(ms, ItunesAtomNames.Composer, tags.Composer);
        WriteTextAtom(ms, ItunesAtomNames.Conductor, tags.Conductor);
        WriteTextAtom(ms, ItunesAtomNames.Comment, tags.Comment);
        WriteTextAtom(ms, ItunesAtomNames.Lyrics, tags.Lyrics);
        WriteTextAtom(ms, ItunesAtomNames.Copyright, tags.Copyright);
        WriteFreeformTextAtom(ms, ItunesMean, "ISRC", tags.Isrc);

        foreach (var (key, value) in TagFieldMapping.EnumerateReplayGainFields(tags))
        {
            WriteFreeformTextAtom(ms, ItunesMean, key, value);
        }

        // Mp4Reader fills these from freeform atoms, so not writing them back drops them on a
        // read-modify-write, which is the ordinary way this library is used.
        foreach (var (key, value) in TagFieldMapping.EnumerateMusicBrainzFields(tags, useVorbisNames: false))
        {
            WriteFreeformTextAtom(ms, ItunesMean, key, value);
        }

        foreach (var (key, value) in tags.CustomFields)
        {
            WriteFreeformTextAtom(ms, ItunesMean, key, value);
        }

        if (tags.Bpm is not null)
            WriteUInt16Atom(ms, ItunesAtomNames.Bpm, (ushort)tags.Bpm.Value);

        if (tags.IsCompilation is not null)
            WriteByteAtom(ms, ItunesAtomNames.Compilation, (byte)(tags.IsCompilation.Value ? 1 : 0));

        foreach (var picture in tags.Pictures)
        {
            var typeIndicator = string.Equals(picture.MimeType, "image/png", StringComparison.OrdinalIgnoreCase) ? 14u : 13u;
            WriteDataAtom(ms, ItunesAtomNames.CoverArt, typeIndicator, picture.Data);
        }

        // Items this writer does not produce — sort names, the encoder tool, the gapless flag — are the
        // user's data and are carried through instead of being dropped.
        var existingIlst = moovAtom.FindChild("udta")?.FindChild("meta")?.FindChild("ilst");
        if (existingIlst is not null)
        {
            foreach (var item in existingIlst.Children)
            {
                if (!IsRegeneratedItem(item.Type))
                    ms.Write(GetAtomBytes(item, moovAtom, moovData));
            }
        }

        ilstData = ms.ToArray();
        return true;
    }

    /// <summary>
    /// Checks the values that the MP4 atoms store in a narrower type than <see cref="MediaTagInfo"/> does.
    /// </summary>
    private static bool TryValidateRanges(MediaTagInfo tags, out string? error)
    {
        foreach (var (name, value) in new[]
        {
            (nameof(MediaTagInfo.TrackNumber), tags.TrackNumber),
            (nameof(MediaTagInfo.TrackTotal), tags.TrackTotal),
            (nameof(MediaTagInfo.DiscNumber), tags.DiscNumber),
            (nameof(MediaTagInfo.DiscTotal), tags.DiscTotal),
            (nameof(MediaTagInfo.Bpm), tags.Bpm),
        })
        {
            if (value > ushort.MaxValue)
            {
                error = $"{name} is {value}, which does not fit in an MP4 tag (the maximum is {ushort.MaxValue}).";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsRegeneratedItem(string atomType)
    {
        return atomType is ItunesAtomNames.Title or ItunesAtomNames.Artist or ItunesAtomNames.Album
            or ItunesAtomNames.AlbumArtist or ItunesAtomNames.Genre or ItunesAtomNames.Year
            or ItunesAtomNames.TrackNumber or ItunesAtomNames.DiscNumber or ItunesAtomNames.Composer
            or ItunesAtomNames.Conductor or ItunesAtomNames.Comment or ItunesAtomNames.Lyrics
            or ItunesAtomNames.Copyright or ItunesAtomNames.Bpm or ItunesAtomNames.Compilation
            or ItunesAtomNames.CoverArt or ItunesAtomNames.Freeform;
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

    private static void WriteFreeformTextAtom(MemoryStream ms, string mean, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var meanBytes = Encoding.UTF8.GetBytes(mean);
        var meanData = new byte[4 + meanBytes.Length]; // version/flags + value
        meanBytes.CopyTo(meanData, 4);
        var meanAtom = BuildAtom("mean", meanData);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var nameData = new byte[4 + nameBytes.Length]; // version/flags + value
        nameBytes.CopyTo(nameData, 4);
        var nameAtom = BuildAtom("name", nameData);

        var valueBytes = Encoding.UTF8.GetBytes(value);
        var dataPayload = new byte[8 + valueBytes.Length]; // type + locale + value
        BinaryPrimitives.WriteUInt32BigEndian(dataPayload, 1); // UTF-8 text
        valueBytes.CopyTo(dataPayload, 8);
        var dataAtom = BuildAtom("data", dataPayload);

        var freeformData = new byte[meanAtom.Length + nameAtom.Length + dataAtom.Length];
        meanAtom.CopyTo(freeformData, 0);
        nameAtom.CopyTo(freeformData, meanAtom.Length);
        dataAtom.CopyTo(freeformData, meanAtom.Length + nameAtom.Length);

        ms.Write(BuildAtom(ItunesAtomNames.Freeform, freeformData));
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
