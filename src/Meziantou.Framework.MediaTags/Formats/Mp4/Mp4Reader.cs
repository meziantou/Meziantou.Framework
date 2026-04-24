using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal sealed class Mp4Reader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo();

            var atoms = Mp4Atom.ReadAtoms(stream, stream.Length);
            tags.Duration = TryReadDuration(atoms);

            // Navigate to moov.udta.meta.ilst
            var ilst = Mp4Atom.FindPath(atoms, "moov", "udta", "meta", "ilst");
            if (ilst is null)
                return MediaTagResult<MediaTagInfo>.Success(tags); // No metadata

            foreach (var item in ilst.Children)
            {
                ProcessIlstItem(item, tags);
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

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

        if (!IsSoundTrack(mediaAtom.FindChild("hdlr")?.Data))
            return null;

        return TryReadDurationFromFullBox(mediaAtom.FindChild("mdhd")?.Data);
    }

    private static bool IsSoundTrack(byte[]? handlerData)
    {
        if (handlerData is null || handlerData.Length < 12)
            return false;

        return handlerData[8] == 's' && handlerData[9] == 'o' && handlerData[10] == 'u' && handlerData[11] == 'n';
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

        return TimeSpan.FromSeconds(duration / (double)timeScale);
    }

    private static TimeSpan? TryReadVersion1Duration(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
            return null;

        var timeScale = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        var duration = BinaryPrimitives.ReadUInt64BigEndian(data[24..]);

        if (timeScale == 0 || duration == 0 || duration == ulong.MaxValue)
            return null;

        return TimeSpan.FromSeconds(duration / (double)timeScale);
    }

    private static void ProcessIlstItem(Mp4Atom item, MediaTagInfo tags)
    {
        // Each ilst child has a "data" sub-atom
        var dataAtom = item.FindChild("data");
        if (dataAtom?.Data is null || dataAtom.Data.Length < 8)
            return;

        // data atom: type indicator (4 bytes) + locale (4 bytes) + value
        var typeIndicator = BinaryPrimitives.ReadUInt32BigEndian(dataAtom.Data);
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
            case ItunesAtomNames.CoverArt:
                // covr: raw image data. Type indicator tells format:
                // 13 = JPEG, 14 = PNG
                var mimeType = typeIndicator switch
                {
                    13 => "image/jpeg",
                    14 => "image/png",
                    _ => "image/jpeg",
                };
                tags.Pictures.Add(new MediaPicture
                {
                    PictureType = MediaPictureType.FrontCover,
                    MimeType = mimeType,
                    Data = valueData.ToArray(),
                });
                break;
            case ItunesAtomNames.Freeform:
                // Freeform atoms: check mean/name sub-atoms
                ProcessFreeformAtom(item, tags);
                break;
        }
    }

    private static void ProcessFreeformAtom(Mp4Atom item, MediaTagInfo tags)
    {
        var meanAtom = item.FindChild("mean");
        var nameAtom = item.FindChild("name");
        var dataAtom = item.FindChild("data");

        if (meanAtom?.Data is null || nameAtom?.Data is null || dataAtom?.Data is null)
            return;

        // mean and name atoms have 4-byte version/flags prefix
        var mean = meanAtom.Data.Length > 4 ? Encoding.UTF8.GetString(meanAtom.Data.AsSpan(4)) : "";
        var name = nameAtom.Data.Length > 4 ? Encoding.UTF8.GetString(nameAtom.Data.AsSpan(4)) : "";

        if (dataAtom.Data.Length < 8)
            return;

        var value = Encoding.UTF8.GetString(dataAtom.Data.AsSpan(8));

        if (mean == "com.apple.iTunes")
        {
            if (string.Equals(name, "MusicBrainz Track Id", StringComparison.OrdinalIgnoreCase))
                tags.MusicBrainzTrackId ??= value;
            else if (string.Equals(name, "MusicBrainz Artist Id", StringComparison.OrdinalIgnoreCase))
                tags.MusicBrainzArtistId ??= value;
            else if (string.Equals(name, "MusicBrainz Album Id", StringComparison.OrdinalIgnoreCase))
                tags.MusicBrainzAlbumId ??= value;
            else if (string.Equals(name, "MusicBrainz Release Group Id", StringComparison.OrdinalIgnoreCase))
                tags.MusicBrainzReleaseGroupId ??= value;
            else if (string.Equals(name, "ISRC", StringComparison.OrdinalIgnoreCase))
                tags.Isrc ??= value;
            else if (string.Equals(name, "REPLAYGAIN_TRACK_GAIN", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseReplayGainValue(value, out var gain))
                    tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackGain = gain };
            }
            else if (string.Equals(name, "REPLAYGAIN_TRACK_PEAK", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
                    tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackPeak = peak };
            }
            else if (string.Equals(name, "REPLAYGAIN_ALBUM_GAIN", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseReplayGainValue(value, out var gain))
                    tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumGain = gain };
            }
            else if (string.Equals(name, "REPLAYGAIN_ALBUM_PEAK", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
                    tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumPeak = peak };
            }
            else
            {
                tags.CustomFields.TryAdd(name, value);
            }
        }
        else
        {
            tags.CustomFields.TryAdd(mean + ":" + name, value);
        }
    }

    private static string ReadUtf8Value(ReadOnlySpan<byte> data) => Encoding.UTF8.GetString(data);

    private static bool TryParseReplayGainValue(string value, out double result)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.EndsWith(" dB", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
