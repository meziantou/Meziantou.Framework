using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal sealed class Mp4Reader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo { NativeFields = [] };

            // Reading is best effort: whatever atoms were parsed before a malformed one still carry usable
            // tags, and unlike the writer nothing is destroyed by using them.
            var atoms = Mp4Atom.ReadAtoms(stream, stream.Length, out _);
            tags.Duration = TryReadDuration(atoms);
            ReadVideoSize(atoms, tags);

            // Navigate to moov.udta.meta.ilst
            var ilst = Mp4Atom.FindPath(atoms, "moov", "udta", "meta", "ilst");
            foreach (var item in ilst?.Children ?? [])
            {
                if (ItunesAtomNames.IsRegenerated(item.Type))
                {
                    ProcessIlstItem(item, tags);
                }
                else if (Mp4Writer.IsValidNativeItemType(item.Type) && TryReadNativeField(stream, item) is { } nativeField)
                {
                    tags.NativeFields.Add(nativeField);
                }
            }

            // iTunes stores its built-in genres as an index in gnre rather than as text in ©gen. The text
            // wins when both are present, whatever their order.
            if (tags.Genre is null && ilst?.FindChild(ItunesAtomNames.GenreId)?.FindChild("data")?.Data is { Length: >= 10 } genreData)
            {
                var genreIndex = BinaryPrimitives.ReadUInt16BigEndian(genreData.AsSpan(8));
                if (genreIndex is > 0 and <= 256)
                    tags.Genre = Id3v1.Id3v1Genres.GetGenre((byte)(genreIndex - 1));
            }

            // QuickTime files store some of the fields as text records of udta. The metadata list takes precedence.
            if (Mp4Atom.FindPath(atoms, "moov", "udta") is { } userData)
                QuickTimeUserData.Apply(userData, tags);

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }

    /// <summary>Reads an item the writer does not regenerate, which it keeps as is.</summary>
    private static MediaNativeField? TryReadNativeField(Stream stream, Mp4Atom item)
    {
        stream.Position = item.Position + item.HeaderSize;
        if (!StreamHelpers.TryReadExact(stream, item.Size - item.HeaderSize, Mp4Atom.MaxMoovSize, out var data))
            return null;

        return new MediaNativeField { TagType = MediaTagType.Mp4, Id = item.Type, Data = data };
    }

    /// <summary>Reads the size of the first video track from its track header, whose width and height are 16.16 fixed-point values.</summary>
    private static void ReadVideoSize(List<Mp4Atom> atoms, MediaTagInfo tags)
    {
        var moovAtom = Mp4Atom.FindPath(atoms, "moov");
        if (moovAtom is null)
            return;

        foreach (var trackAtom in moovAtom.Children)
        {
            if (trackAtom.Type != "trak" || !HasHandler(trackAtom.FindChild("mdia")?.FindChild("hdlr")?.Data, "vide"u8))
                continue;

            var trackHeader = trackAtom.FindChild("tkhd")?.Data;
            if (trackHeader is not { Length: > 0 })
                continue;

            var sizeOffset = trackHeader[0] == 1 ? 88 : 76;
            if (trackHeader.Length < sizeOffset + 8)
                continue;

            var width = BinaryPrimitives.ReadUInt32BigEndian(trackHeader.AsSpan(sizeOffset)) >> 16;
            var height = BinaryPrimitives.ReadUInt32BigEndian(trackHeader.AsSpan(sizeOffset + 4)) >> 16;
            if (width > 0 && height > 0)
            {
                tags.VideoWidth = (int)width;
                tags.VideoHeight = (int)height;
                return;
            }
        }
    }

    private static bool HasHandler(byte[]? handlerData, ReadOnlySpan<byte> handlerType)
        => handlerData is { Length: >= 12 } && handlerData.AsSpan(8, 4).SequenceEqual(handlerType);

    private static TimeSpan? TryReadDuration(List<Mp4Atom> atoms)
    {
        var moovAtom = Mp4Atom.FindPath(atoms, "moov");
        if (moovAtom is null)
            return null;

        foreach (var atom in moovAtom.Children)
        {
            if (atom.Type != "trak")
                continue;

            var trackDuration = TryReadAudioTrackDuration(atom);
            if (trackDuration is not null)
                return trackDuration;
        }

        return TryReadDurationFromFullBox(moovAtom.FindChild("mvhd")?.Data);
    }

    private static TimeSpan? TryReadAudioTrackDuration(Mp4Atom trackAtom)
    {
        var mediaAtom = trackAtom.FindChild("mdia");
        if (mediaAtom is null)
            return null;

        if (!HasHandler(mediaAtom.FindChild("hdlr")?.Data, "soun"u8))
            return null;

        return TryReadDurationFromFullBox(mediaAtom.FindChild("mdhd")?.Data);
    }

    private static TimeSpan? TryReadDurationFromFullBox(byte[]? data)
    {
        if (data is null || data.Length < 20)
            return null;

        var version = data[0];
        return version switch
        {
            0 => TryReadVersion0Duration(data),
            1 => TryReadVersion1Duration(data),
            _ => null,
        };
    }

    private static TimeSpan? TryReadVersion0Duration(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20)
            return null;

        var timeScale = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        var duration = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);

        if (timeScale == 0 || duration == 0 || duration == uint.MaxValue)
            return null;

        return DurationHelpers.FromSeconds(duration / (double)timeScale);
    }

    private static TimeSpan? TryReadVersion1Duration(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
            return null;

        var timeScale = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        var duration = BinaryPrimitives.ReadUInt64BigEndian(data[24..]);

        if (timeScale == 0 || duration == 0 || duration == ulong.MaxValue)
            return null;

        return DurationHelpers.FromSeconds(duration / (double)timeScale);
    }

    private static void ProcessIlstItem(Mp4Atom item, MediaTagInfo tags)
    {
        // covr holds one data atom per image
        if (item.Type == ItunesAtomNames.CoverArt)
        {
            foreach (var child in item.Children)
            {
                if (child.Type == "data" && child.Data is { Length: >= 8 } pictureData)
                    AddCoverArt(pictureData, tags);
            }

            return;
        }

        // Each ilst child has a "data" sub-atom
        var dataAtom = item.FindChild("data");
        if (dataAtom?.Data is null || dataAtom.Data.Length < 8)
            return;

        // data atom: type indicator (4 bytes) + locale (4 bytes) + value
        var valueData = dataAtom.Data.AsSpan(8);

        switch (item.Type)
        {
            case ItunesAtomNames.Title:
                tags.Title ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Artist:
                tags.Artist ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Album:
                tags.Album ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.AlbumArtist:
                tags.AlbumArtist ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Genre:
                tags.Genre ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Year:
                if (tags.Year is null)
                {
                    var yearStr = ReadUtf8Value(valueData);
                    if (yearStr.Length >= 4 && int.TryParse(yearStr.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                        tags.Year = year;
                }
                break;
            case ItunesAtomNames.TrackNumber:
                // trkn: 8 bytes: 2 padding + 2 track number + 2 track total + 2 padding
                if (tags.TrackNumber is null && valueData.Length >= 6)
                {
                    var trackNum = BinaryPrimitives.ReadUInt16BigEndian(valueData[2..]);
                    var trackTotal = BinaryPrimitives.ReadUInt16BigEndian(valueData[4..]);
                    if (trackNum > 0) tags.TrackNumber = trackNum;
                    if (trackTotal > 0) tags.TrackTotal = trackTotal;
                }
                break;
            case ItunesAtomNames.DiscNumber:
                // disk: similar to trkn
                if (tags.DiscNumber is null && valueData.Length >= 6)
                {
                    var discNum = BinaryPrimitives.ReadUInt16BigEndian(valueData[2..]);
                    var discTotal = BinaryPrimitives.ReadUInt16BigEndian(valueData[4..]);
                    if (discNum > 0) tags.DiscNumber = discNum;
                    if (discTotal > 0) tags.DiscTotal = discTotal;
                }
                break;
            case ItunesAtomNames.Composer:
                tags.Composer ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Conductor:
                tags.Conductor ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Comment:
                tags.Comment ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Lyrics:
                tags.Lyrics ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Copyright:
                tags.Copyright ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.Bpm:
                // tmpo: 2 bytes big-endian uint16
                if (tags.Bpm is null && valueData.Length >= 2)
                    tags.Bpm = BinaryPrimitives.ReadUInt16BigEndian(valueData);
                break;
            case ItunesAtomNames.Compilation:
                // cpil: 1 byte boolean
                if (tags.IsCompilation is null && valueData.Length >= 1)
                    tags.IsCompilation = valueData[0] != 0;
                break;
            case ItunesAtomNames.MediaKind:
                if (tags.MediaKind is null && TryReadInteger(valueData, out var mediaKind) && mediaKind <= byte.MaxValue)
                    tags.MediaKind = (MediaKind)mediaKind;
                break;
            case ItunesAtomNames.Description:
                tags.Description ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.LongDescription:
                tags.LongDescription ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.TvShow:
                tags.TvShow ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.TvNetwork:
                tags.TvNetwork ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.TvEpisodeId:
                tags.TvEpisodeId ??= ReadUtf8Value(valueData);
                break;
            case ItunesAtomNames.TvSeason:
                if (tags.TvSeason is null && TryReadInteger(valueData, out var season) && season <= int.MaxValue)
                    tags.TvSeason = (int)season;
                break;
            case ItunesAtomNames.TvEpisode:
                if (tags.TvEpisode is null && TryReadInteger(valueData, out var episode) && episode <= int.MaxValue)
                    tags.TvEpisode = (int)episode;
                break;
            case ItunesAtomNames.Freeform:
                // Freeform atoms: check mean/name sub-atoms
                ProcessFreeformAtom(item, tags);
                break;
        }
    }

    private static void AddCoverArt(byte[] dataAtomData, MediaTagInfo tags)
    {
        // data atom: type indicator (4 bytes) + locale (4 bytes) + image
        var typeIndicator = BinaryPrimitives.ReadUInt32BigEndian(dataAtomData);
        tags.Pictures.Add(new MediaPicture
        {
            PictureType = MediaPictureType.FrontCover,
            MimeType = GetCoverArtMimeType(typeIndicator),
            Data = dataAtomData.AsSpan(8).ToArray(),
        });
    }

    internal static string GetCoverArtMimeType(uint typeIndicator) => typeIndicator switch
    {
        12 => "image/gif",
        14 => "image/png",
        27 => "image/bmp",
        _ => "image/jpeg",
    };

    private static void ProcessFreeformAtom(Mp4Atom item, MediaTagInfo tags)
    {
        var meanAtom = item.FindChild("mean");
        var nameAtom = item.FindChild("name");
        var dataAtom = item.FindChild("data");

        if (meanAtom?.Data is null || nameAtom?.Data is null || dataAtom?.Data is null)
            return;

        // mean and name atoms have 4-byte version/flags prefix
        var mean = ReadFreeformText(meanAtom.Data);
        var name = ReadFreeformText(nameAtom.Data);
        var value = ReadFreeformDataValue(dataAtom.Data);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
            return;

        if (string.Equals(mean, "com.apple.iTunes", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(name, "ISRC", StringComparison.OrdinalIgnoreCase))
                tags.Isrc ??= value;
            else if (!TagFieldMapping.TryApplySharedField(name, value, tags))
                tags.CustomFields.TryAdd(name, value);
        }
        else
        {
            tags.CustomFields.TryAdd(mean + ":" + name, value);
        }
    }

    /// <summary>Reads a big-endian unsigned integer of 1, 2, 4 or 8 bytes, the sizes iTunes uses for integer items.</summary>
    private static bool TryReadInteger(ReadOnlySpan<byte> data, out ulong value)
    {
        value = data.Length switch
        {
            1 => data[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(data),
            4 => BinaryPrimitives.ReadUInt32BigEndian(data),
            8 => BinaryPrimitives.ReadUInt64BigEndian(data),
            _ => ulong.MaxValue,
        };

        return data.Length is 1 or 2 or 4 or 8;
    }

    private static string ReadUtf8Value(ReadOnlySpan<byte> data) => TrimTrailingNullCharacters(Encoding.UTF8.GetString(data));

    private static string ReadFreeformText(byte[] atomData)
    {
        if (atomData.Length <= 4)
            return "";

        return TrimTrailingNullCharacters(Encoding.UTF8.GetString(atomData.AsSpan(4)));
    }

    private static string ReadFreeformDataValue(byte[] atomData)
    {
        if (atomData.Length < 8)
            return "";

        var typeIndicator = BinaryPrimitives.ReadUInt32BigEndian(atomData);
        var valueData = atomData.AsSpan(8);
        var text = typeIndicator switch
        {
            2 => Encoding.BigEndianUnicode.GetString(valueData),
            _ => Encoding.UTF8.GetString(valueData),
        };

        return TrimTrailingNullCharacters(text);
    }

    private static string TrimTrailingNullCharacters(string value) => value.TrimEnd('\0');
}
