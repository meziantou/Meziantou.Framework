using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.MediaTags.Fuzz;

/// <summary>
/// Writes arbitrary tag values to the test files of a format. Writing either fails, for the values the format documents it
/// cannot store, or produces a file that reads back with the values written, changed only the way the format changes them,
/// whose tags are a fixed point, and whose audio is the one of the original file.
/// </summary>
/// <remarks>
/// <para>The way each format changes a value, as implemented by its reader and writer:</para>
/// <list type="bullet">
/// <item>Text is stored as UTF-8, so an unpaired surrogate reads back as U+FFFD. ID3v2 and MP4 values lose their trailing
/// null characters, and an empty value is not written (an empty text is the same as a missing one).</item>
/// <item>ID3v2 stores the track and disc numbers as "number/total", where "0/total" is a total without a number. MP4 stores
/// them, the BPM included, as 16-bit values where 0 is a missing value; a larger value is refused.</item>
/// <item>ReplayGain gains are stored with 2 decimals and peaks with 6.</item>
/// <item>A picture MIME type is ASCII. MP4 stores neither the picture type nor the description, and only knows the PNG, GIF,
/// BMP and JPEG types. ID3v2 drops a picture without data.</item>
/// <item>A custom field is a TXXX frame (ID3v2), a Vorbis comment, or a freeform atom (MP4), whose name a "mean:name" key gives
/// when the namespace looks like a reverse domain name. MP4 drops a field with an empty name or value. A custom field named
/// like a field the reader knows is read as that field, which is checked by the fixed point only.</item>
/// <item>ID3v2 refuses a null character in a custom field name or in a picture MIME type or description, and Vorbis comments
/// refuse a '=' in a field name: they would end the name early.</item>
/// </list>
/// </remarks>
public static partial class MediaTagsFuzzTargets
{
    /// <summary>The most characters or bytes the long values of an input add up to, so that no tag reaches a size limit of its format.</summary>
    private const int MaxLongValueBudget = 1 << 20;

    private const string ItunesMean = "com.apple.iTunes";

    private static readonly double[] SpecialDoubles = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -0d, double.MaxValue, double.MinValue, double.Epsilon, 1e300, -1e300, -6.5, 0.98, 1, 0.005, -0.005, 0.0000005, 123456789.125];

    private static readonly string[] ReservedId3v2Names = ["comment", .. SharedFieldNames()];

    private static readonly string[] ReservedVorbisNames =
    [
        "TITLE", "ARTIST", "ALBUM", "ALBUMARTIST", "GENRE", "DATE", "TRACKNUMBER", "TRACKTOTAL", "DISCNUMBER", "DISCTOTAL", "COMMENT", "DESCRIPTION", "LYRICS", "UNSYNCEDLYRICS",
        "ISRC", "COMPOSER", "CONDUCTOR", "COPYRIGHT", "BPM", "COMPILATION", "METADATA_BLOCK_PICTURE", .. SharedFieldNames(),
    ];

    private static readonly string[] ReservedMp4Names = ["ISRC", .. SharedFieldNames()];

    /// <summary>The test files, with their format.</summary>
    private static readonly List<(string Name, byte[] Data, MediaFormat Format)> ValueTestFiles = [.. GetTestFiles().Select(file => (file.Name, file.Data, Format: MediaFile.DetectFormat(new MemoryStream(file.Data, writable: false)))).Where(file => file.Format is not null).Select(file => (file.Name, file.Data, file.Format!.Value))];

    private enum TagFamily
    {
        Id3v2,
        VorbisComment,
        Mp4,
    }

    private static string[] SharedFieldNames() =>
    [
        "REPLAYGAIN_TRACK_GAIN", "REPLAYGAIN_TRACK_PEAK", "REPLAYGAIN_ALBUM_GAIN", "REPLAYGAIN_ALBUM_PEAK",
        "MUSICBRAINZ_TRACKID", "MUSICBRAINZ_ARTISTID", "MUSICBRAINZ_ALBUMID", "MUSICBRAINZ_RELEASEGROUPID",
        "MusicBrainz Track Id", "MusicBrainz Artist Id", "MusicBrainz Album Id", "MusicBrainz Release Group Id",
    ];

    /// <summary>
    /// <c>format, options, values</c>: <c>format</c> selects the format, whose every test file is written. <c>options</c> gives
    /// the write options (low 3 bits, as the mode of the write target) and whether a custom field may be named like a field
    /// the reader knows (bit 3). <c>values</c> is a list of fields, read by <see cref="ReadTagValues"/>.
    /// </summary>
    private static void WriteValues(ReadOnlySpan<byte> data)
    {
        var format = Formats[FuzzInput.ReadByte(ref data) % Formats.Length];
        var options = FuzzInput.ReadByte(ref data);
        var writeOptions = new MediaTagWriteOptions
        {
            WriteId3v1Tag = (options & 0x01) is 0,
            Id3v2PaddingSize = ((options >> 1) & 0x03) switch
            {
                0 => 1024,
                1 => 0,
                2 => 1,
                _ => 4096,
            },
        };
        var allowReservedNames = (options & 0x08) is not 0;
        var tags = ReadTagValues(ref data, format, allowReservedNames);
        var expectedError = GetExpectedWriteError(tags, format);
        var expected = expectedError is null ? Normalize(tags, format) : null;

        foreach (var (name, file, fileFormat) in ValueTestFiles)
        {
            if (fileFormat != format)
                continue;

            WriteValues(name, file, format, tags, writeOptions, expectedError, allowReservedNames ? null : expected);
        }
    }

    private static void WriteValues(string fileName, byte[] file, MediaFormat format, MediaTagInfo tags, MediaTagWriteOptions options, string? expectedError, MediaTagInfo? expected)
    {
        var output = new MemoryStream();
        var result = MediaFile.WriteTags(new MemoryStream(file, writable: false), output, tags, format, options);
        if (expectedError is not null)
        {
            FuzzAssert.False(result.IsSuccess, $"{fileName}: the write succeeds, but the tags hold a value the format cannot store ({expectedError})");
            FuzzAssert.Equal(MediaTagError.InvalidTagData, result.Error, $"{fileName}: the write of a value the format cannot store ({expectedError}) fails with another error: {result.ErrorMessage}");
            return;
        }

        FuzzAssert.True(result.IsSuccess, $"{fileName}: the write fails: {result.Error} {result.ErrorMessage}");
        var written = output.ToArray();
        if (format is not MediaFormat.Mp3)
        {
            FuzzAssert.Equal<MediaFormat?>(format, Detect(written, 0), $"{fileName}: the format detected in the written file differs");
        }

        var readBack = MediaFile.ReadTags(new MemoryStream(written, writable: false), format);
        FuzzAssert.True(readBack.IsSuccess, $"{fileName}: the written file does not read back: {readBack.Error} {readBack.ErrorMessage}");
        var readBackTags = readBack.Value;
        if (expected is not null)
        {
            // The duration comes from the audio: no value of the input sets it
            expected.Format = format;
            expected.Duration = readBackTags.Duration;
            FuzzAssert.Equal(Describe(expected), Describe(readBackTags), $"{fileName}: the written values do not read back");
        }

        var rewritten = new MemoryStream();
        var rewriteResult = MediaFile.WriteTags(new MemoryStream(written, writable: false), rewritten, readBackTags, format, options);
        FuzzAssert.True(rewriteResult.IsSuccess, $"{fileName}: the written file cannot be written again: {rewriteResult.Error} {rewriteResult.ErrorMessage}");
        var reread = MediaFile.ReadTags(new MemoryStream(rewritten.ToArray(), writable: false), format);
        FuzzAssert.True(reread.IsSuccess, $"{fileName}: the file written twice does not read back: {reread.Error} {reread.ErrorMessage}");
        FuzzAssert.Equal(Describe(readBackTags), Describe(reread.Value), $"{fileName}: writing the tags read from the written file changes them");

        // Whatever the values, the rest of the file is the one of the original: removing the tags of both gives the same file
        var strippedOriginal = new MemoryStream();
        var strippedWritten = new MemoryStream();
        FuzzAssert.True(MediaFile.RemoveTags(new MemoryStream(file, writable: false), strippedOriginal, format).IsSuccess, $"{fileName}: the tags of the file cannot be removed");
        FuzzAssert.True(MediaFile.RemoveTags(new MemoryStream(written, writable: false), strippedWritten, format).IsSuccess, $"{fileName}: the tags of the written file cannot be removed");
        FuzzAssert.True(strippedOriginal.ToArray().AsSpan().SequenceEqual(strippedWritten.ToArray()), $"{fileName}: the written file differs from the original once their tags are removed");
    }

    /// <summary>
    /// Reads fields until the end of the input. Each one is a byte that selects the field, then its value: a text (<see cref="ReadValueText"/>),
    /// a number (<see cref="ReadValueNumber"/>), a byte, or for a picture its type, MIME type, description and data.
    /// </summary>
    private static MediaTagInfo ReadTagValues(ref ReadOnlySpan<byte> data, MediaFormat format, bool allowReservedNames)
    {
        var tags = new MediaTagInfo();
        var budget = MaxLongValueBudget;
        while (!data.IsEmpty)
        {
            var field = FuzzInput.ReadByte(ref data);
            switch (field % 26)
            {
                case 0: tags.Title = ReadValueText(ref data, ref budget); break;
                case 1: tags.Artist = ReadValueText(ref data, ref budget); break;
                case 2: tags.Album = ReadValueText(ref data, ref budget); break;
                case 3: tags.AlbumArtist = ReadValueText(ref data, ref budget); break;
                case 4: tags.Genre = ReadValueText(ref data, ref budget); break;
                case 5: tags.Comment = ReadValueText(ref data, ref budget); break;
                case 6: tags.Lyrics = ReadValueText(ref data, ref budget); break;
                case 7: tags.Isrc = ReadValueText(ref data, ref budget); break;
                case 8: tags.Composer = ReadValueText(ref data, ref budget); break;
                case 9: tags.Conductor = ReadValueText(ref data, ref budget); break;
                case 10: tags.Copyright = ReadValueText(ref data, ref budget); break;
                case 11: tags.MusicBrainzTrackId = ReadValueText(ref data, ref budget); break;
                case 12: tags.MusicBrainzArtistId = ReadValueText(ref data, ref budget); break;
                case 13: tags.MusicBrainzAlbumId = ReadValueText(ref data, ref budget); break;
                case 14: tags.MusicBrainzReleaseGroupId = ReadValueText(ref data, ref budget); break;
                case 15: SetNumber(ReadValueNumber(ref data), value => tags.Year = value, maxValue: 9999, nameof(MediaTagInfo.Year)); break;
                case 16: SetNumber(ReadValueNumber(ref data), value => tags.TrackNumber = value, maxValue: int.MaxValue, nameof(MediaTagInfo.TrackNumber)); break;
                case 17: SetNumber(ReadValueNumber(ref data), value => tags.TrackTotal = value, maxValue: int.MaxValue, nameof(MediaTagInfo.TrackTotal)); break;
                case 18: SetNumber(ReadValueNumber(ref data), value => tags.DiscNumber = value, maxValue: int.MaxValue, nameof(MediaTagInfo.DiscNumber)); break;
                case 19: SetNumber(ReadValueNumber(ref data), value => tags.DiscTotal = value, maxValue: int.MaxValue, nameof(MediaTagInfo.DiscTotal)); break;
                case 20: SetNumber(ReadValueNumber(ref data), value => tags.Bpm = value, maxValue: int.MaxValue, nameof(MediaTagInfo.Bpm)); break;
                case 21:
                    tags.IsCompilation = (FuzzInput.ReadByte(ref data) % 3) switch
                    {
                        0 => null,
                        1 => true,
                        _ => false,
                    };
                    break;

                case 22:
                    var components = FuzzInput.ReadByte(ref data);
                    tags.ReplayGain = (components & 0x10) is not 0 ? null : new ReplayGainInfo
                    {
                        TrackGain = (components & 0x01) is 0 ? null : ReadValueDouble(ref data),
                        TrackPeak = (components & 0x02) is 0 ? null : ReadValueDouble(ref data),
                        AlbumGain = (components & 0x04) is 0 ? null : ReadValueDouble(ref data),
                        AlbumPeak = (components & 0x08) is 0 ? null : ReadValueDouble(ref data),
                    };
                    break;

                case 23:
                    var pictureType = (MediaPictureType)FuzzInput.ReadByte(ref data);
                    var nulls = FuzzInput.ReadByte(ref data);
                    var mimeType = ReadValueText(ref data, ref budget);
                    var description = ReadValueText(ref data, ref budget);
                    tags.Pictures.Add(new MediaPicture
                    {
                        PictureType = pictureType,
                        MimeType = (nulls & 0x01) is 0 ? mimeType : null,
                        Description = (nulls & 0x02) is 0 ? description : null,
                        Data = ReadValueBytes(ref data, ref budget),
                    });
                    break;

                case 24:
                    var key = ReadValueText(ref data, ref budget);
                    var value = ReadValueText(ref data, ref budget);
                    if (allowReservedNames || !IsReservedCustomFieldName(format, key))
                    {
                        tags.CustomFields[key] = value;
                    }

                    break;

                default:
                    // Clears a field, so that a field can be missing after an input set it
                    switch (FuzzInput.ReadByte(ref data) % 4)
                    {
                        case 0: tags.Pictures.Clear(); break;
                        case 1: tags.CustomFields.Clear(); break;
                        case 2: tags.TrackNumber = null; break;
                        default: tags.Title = null; break;
                    }

                    break;
            }
        }

        return tags;
    }

    /// <summary>Sets a number, and checks that the property refuses it exactly when it is out of its documented range.</summary>
    private static void SetNumber(int value, Action<int> setter, int maxValue, string name)
    {
        var isValid = value >= 0 && value <= maxValue;
        try
        {
            setter(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            FuzzAssert.False(isValid, $"{name} refuses {value}");
            return;
        }

        FuzzAssert.True(isValid, $"{name} accepts {value}");
    }

    /// <summary>
    /// A length byte, then the text: below 0xC0, the length of the UTF-8 (<see cref="FuzzText"/>) text that follows. Otherwise the
    /// text of (low 4 bits + 1) bytes, repeated to (next byte + 1) KB, so that a value can exceed 64 KB.
    /// </summary>
    private static string ReadValueText(ref ReadOnlySpan<byte> data, ref int budget)
        => FuzzText.Decode(ReadValueBytes(ref data, ref budget));

    private static byte[] ReadValueBytes(ref ReadOnlySpan<byte> data, ref int budget)
    {
        var length = FuzzInput.ReadByte(ref data);
        if (length < 0xC0)
            return Take(ref data, length).ToArray();

        var pattern = Take(ref data, (length & 0x0F) + 1);
        var repeatedLength = (FuzzInput.ReadByte(ref data) + 1) * 1024;
        if (pattern.IsEmpty || repeatedLength > budget)
            return pattern.ToArray();

        budget -= repeatedLength;
        var result = new byte[repeatedLength];
        for (var offset = 0; offset < result.Length; offset += pattern.Length)
        {
            pattern[..Math.Min(pattern.Length, result.Length - offset)].CopyTo(result.AsSpan(offset));
        }

        return result;

        static ReadOnlySpan<byte> Take(ref ReadOnlySpan<byte> data, int length)
        {
            var result = data[..Math.Min(length, data.Length)];
            data = data[result.Length..];
            return result;
        }
    }

    /// <summary>A byte below 0xF0 is its value; the others select a limit of the formats, or read a little-endian 32-bit value.</summary>
    private static int ReadValueNumber(ref ReadOnlySpan<byte> data)
    {
        var value = FuzzInput.ReadByte(ref data);
        return value switch
        {
            0xFF => int.MaxValue,
            0xFE => -1,
            0xFD => int.MinValue,
            0xFC => ushort.MaxValue,
            0xFB => ushort.MaxValue + 1,
            0xFA => 9999,
            0xF9 => 10000,
            0xF8 => 255,
            0xF7 => 256,
            0xF0 => FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8) | (FuzzInput.ReadByte(ref data) << 16) | (FuzzInput.ReadByte(ref data) << 24),
            _ => value,
        };
    }

    /// <summary>A byte below 0x80 selects a special value; otherwise the next 8 bytes are the little-endian bits of the value.</summary>
    private static double ReadValueDouble(ref ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data);
        if (selector < 0x80)
            return SpecialDoubles[selector % SpecialDoubles.Length];

        var bits = 0L;
        for (var i = 0; i < 8; i++)
        {
            bits |= (long)FuzzInput.ReadByte(ref data) << (i * 8);
        }

        return BitConverter.Int64BitsToDouble(bits);
    }

    private static TagFamily GetFamily(MediaFormat format) => format switch
    {
        MediaFormat.Mp3 or MediaFormat.Wav or MediaFormat.Aiff => TagFamily.Id3v2,
        MediaFormat.OggVorbis or MediaFormat.OggOpus or MediaFormat.Flac => TagFamily.VorbisComment,
        _ => TagFamily.Mp4,
    };

    /// <summary>Gets why the write must fail, or <see langword="null"/> when the format can store every value.</summary>
    private static string? GetExpectedWriteError(MediaTagInfo tags, MediaFormat format)
    {
        switch (GetFamily(format))
        {
            case TagFamily.Id3v2:
                if (tags.CustomFields.Keys.Any(key => key.Contains('\0', StringComparison.Ordinal)))
                    return "a null character in a custom field name";

                if (tags.Pictures.Any(picture => picture.MimeType?.Contains('\0', StringComparison.Ordinal) is true || picture.Description?.Contains('\0', StringComparison.Ordinal) is true))
                    return "a null character in a picture MIME type or description";

                return null;

            case TagFamily.VorbisComment:
                return tags.CustomFields.Keys.Any(key => key.Contains('=', StringComparison.Ordinal)) ? "a '=' in a custom field name" : null;

            default:
                return new[] { tags.TrackNumber, tags.TrackTotal, tags.DiscNumber, tags.DiscTotal, tags.Bpm }.Any(value => value > ushort.MaxValue) ? "a number larger than 65535" : null;
        }
    }

    /// <summary>Gets the tags the written file must read back as, from the way the format stores each value (see the remarks of the class).</summary>
    private static MediaTagInfo Normalize(MediaTagInfo tags, MediaFormat format)
    {
        var family = GetFamily(format);
        var result = new MediaTagInfo
        {
            Title = NormalizeText(family, tags.Title),
            Artist = NormalizeText(family, tags.Artist),
            Album = NormalizeText(family, tags.Album),
            AlbumArtist = NormalizeText(family, tags.AlbumArtist),
            Genre = NormalizeText(family, tags.Genre),
            Year = tags.Year,
            Comment = NormalizeText(family, tags.Comment),
            Lyrics = NormalizeText(family, tags.Lyrics),
            Isrc = NormalizeText(family, tags.Isrc),
            Composer = NormalizeText(family, tags.Composer),
            Conductor = NormalizeText(family, tags.Conductor),
            Copyright = NormalizeText(family, tags.Copyright),
            Bpm = tags.Bpm,
            IsCompilation = tags.IsCompilation,
            MusicBrainzTrackId = NormalizeText(family, tags.MusicBrainzTrackId),
            MusicBrainzArtistId = NormalizeText(family, tags.MusicBrainzArtistId),
            MusicBrainzAlbumId = NormalizeText(family, tags.MusicBrainzAlbumId),
            MusicBrainzReleaseGroupId = NormalizeText(family, tags.MusicBrainzReleaseGroupId),
        };

        (result.TrackNumber, result.TrackTotal) = NormalizeNumberPair(family, tags.TrackNumber, tags.TrackTotal);
        (result.DiscNumber, result.DiscTotal) = NormalizeNumberPair(family, tags.DiscNumber, tags.DiscTotal);

        if (tags.ReplayGain is { } replayGain && (replayGain.TrackGain is not null || replayGain.TrackPeak is not null || replayGain.AlbumGain is not null || replayGain.AlbumPeak is not null))
        {
            result.ReplayGain = new ReplayGainInfo
            {
                TrackGain = Round(replayGain.TrackGain, "F2"),
                TrackPeak = Round(replayGain.TrackPeak, "F6"),
                AlbumGain = Round(replayGain.AlbumGain, "F2"),
                AlbumPeak = Round(replayGain.AlbumPeak, "F6"),
            };
        }

        foreach (var picture in tags.Pictures)
        {
            var mimeType = Ascii(picture.MimeType ?? "image/jpeg");
            switch (family)
            {
                case TagFamily.Id3v2 when picture.Data.Length is 0:
                    break;

                case TagFamily.Id3v2 or TagFamily.VorbisComment:
                    result.Pictures.Add(new MediaPicture { PictureType = picture.PictureType, MimeType = mimeType, Description = Utf8(picture.Description ?? ""), Data = picture.Data });
                    break;

                default:
                    result.Pictures.Add(new MediaPicture { PictureType = MediaPictureType.FrontCover, MimeType = GetMp4MimeType(picture.MimeType), Data = picture.Data });
                    break;
            }
        }

        foreach (var (key, value) in tags.CustomFields)
        {
            if (NormalizeCustomField(family, key, value) is var (normalizedKey, normalizedValue))
            {
                result.CustomFields.TryAdd(normalizedKey, normalizedValue);
            }
        }

        return result;

        static double? Round(double? value, string format) => value is { } number ? double.Parse(number.ToString(format, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture) : null;

        static string GetMp4MimeType(string? mimeType)
        {
            foreach (var known in (string[])["image/png", "image/gif", "image/bmp"])
            {
                if (string.Equals(mimeType, known, StringComparison.OrdinalIgnoreCase))
                    return known;
            }

            return "image/jpeg";
        }
    }

    private static string? NormalizeText(TagFamily family, string? value) => value is null ? null : family is TagFamily.VorbisComment ? Utf8(value) : Utf8(value).TrimEnd('\0');

    private static (int? Number, int? Total) NormalizeNumberPair(TagFamily family, int? number, int? total) => family switch
    {
        TagFamily.Id3v2 when total is not null && number is 0 => (null, total),
        TagFamily.Mp4 => (number is 0 ? null : number, total is 0 ? null : total),
        _ => (number, total),
    };

    /// <summary>Gets the name and the value a custom field reads back as, or <see langword="null"/> when it is not stored.</summary>
    private static (string Key, string Value)? NormalizeCustomField(TagFamily family, string key, string value)
    {
        switch (family)
        {
            case TagFamily.Id3v2:
                return (Utf8(key), Utf8(value).TrimEnd('\0'));

            case TagFamily.VorbisComment:
                return (Utf8(key), Utf8(value));

            default:
                var (mean, name) = SplitMp4CustomFieldName(key);
                mean = Utf8(mean).TrimEnd('\0');
                name = Utf8(name).TrimEnd('\0');
                value = Utf8(value).TrimEnd('\0');
                if (name.Length is 0 || value.Length is 0)
                    return null;

                return (string.Equals(mean, ItunesMean, StringComparison.OrdinalIgnoreCase) ? name : mean + ":" + name, value);
        }
    }

    /// <summary>Gets the namespace and the name of the freeform atom an MP4 custom field is stored in, as the writer does.</summary>
    private static (string Mean, string Name) SplitMp4CustomFieldName(string key)
    {
        var separator = key.IndexOf(':', StringComparison.Ordinal);
        if (separator > 0 && separator < key.Length - 1)
        {
            var mean = key.AsSpan(0, separator);
            if (mean.Contains('.') && !mean.Contains(' '))
                return (key[..separator], key[(separator + 1)..]);
        }

        return (ItunesMean, key);
    }

    /// <summary>Gets whether a custom field reads back as another field, or shadows one.</summary>
    private static bool IsReservedCustomFieldName(MediaFormat format, string key)
    {
        var family = GetFamily(format);
        if (NormalizeCustomField(family, key, "value") is not var (normalizedKey, _))
            return false;

        var reservedNames = family switch
        {
            TagFamily.Id3v2 => ReservedId3v2Names,
            TagFamily.VorbisComment => ReservedVorbisNames,
            _ => ReservedMp4Names,
        };

        return reservedNames.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase);
    }

    private static string Utf8(string value) => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value));

    private static string Ascii(string value) => Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(value));

    private static IEnumerable<FuzzSeed> GetWriteValuesSeeds()
    {
        var edgeValues = new ValueWriter()
            .Text(0, "Title\0with\0nulls\0\0")
            .Text(1, "A=B/C:D\nE\r\nF")
            .Text(2, "\uD800lone\uDFFFsurrogates\uDBFF")
            .Text(3, "")
            .Text(4, "(12)")
            .Text(5, "Comment\0")
            .Text(6, "Line 1\nLine 2\n")
            .Text(7, "\0")
            .LongText(8, "\u00E9\U0001F3B5x", 64)
            .LongText(9, "c", 70)
            .Text(10, "\u00A9 2024")
            .Text(11, "f1c4a1d2-9d4b-4e0a-8f3e-1c2b3a4d5e6f")
            .Number(15, 0)
            .Number(16, 12)
            .Number(17, 3)
            .Number(18, 0)
            .Number(19, 0xFC)
            .Number(20, 0)
            .Byte(21, 2)
            .ReplayGain(0x0F, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300)
            .Picture(255, "image/\u00E9", "Cover\uD800", [])
            .Picture(3, "IMAGE/PNG", "", [0x89, 0x50, 0x4E, 0x47])
            .Custom("", "empty name")
            .Custom("org.example.tool:Name", "freeform")
            .Custom("com.apple.iTunes:Other", "itunes")
            .Custom("Name\uD800", "value\0")
            .Custom("EMPTY", "")
            .LongCustom("LONG", "0123456789abcdef", 80);

        var limits = new ValueWriter()
            .Number(15, 0xFA)
            .Number(16, 0xFF)
            .Number(17, 0xFF)
            .Number(18, 0xFB)
            .Number(19, 0xF7)
            .Number(20, 0xFF)
            .ReplayGain(0x0F, double.MaxValue, double.Epsilon, -0.005, 0.0000005);

        var outOfRange = new ValueWriter()
            .Number(15, 0xF9)
            .Number(16, 0xFE)
            .Number(20, 0xFD)
            .Text(0, "Title");

        var nameWithEquals = new ValueWriter().Text(0, "Title").Custom("A=B", "C");
        var nameWithNull = new ValueWriter().Text(0, "Title").Custom("Null\0Name", "value");
        var mimeTypeWithNull = new ValueWriter().Text(0, "Title").Picture(3, "image/\0png", "", [1, 2, 3]);
        var descriptionWithNull = new ValueWriter().Text(0, "Title").Picture(3, "image/png", "Desc\0ription", [1, 2, 3]);

        var reserved = new ValueWriter()
            .Text(5, "Comment")
            .Custom("comment", "TXXX comment")
            .Custom("DATE", "not a date")
            .Custom("TRACKNUMBER", "3/12")
            .Custom("ISRC", "USRC17607839")
            .Custom("com.apple.iTunes:REPLAYGAIN_TRACK_GAIN", "-1.00 dB")
            .Custom("MusicBrainz Track Id", "id");

        var full = new ValueWriter();
        var fullTags = CreateFullTags();
        foreach (var (field, value) in ((int Field, string? Value)[])[(0, fullTags.Title), (1, fullTags.Artist), (2, fullTags.Album), (3, fullTags.AlbumArtist), (4, fullTags.Genre), (5, fullTags.Comment), (6, fullTags.Lyrics), (7, fullTags.Isrc), (8, fullTags.Composer), (9, fullTags.Conductor), (10, fullTags.Copyright), (11, fullTags.MusicBrainzTrackId), (12, fullTags.MusicBrainzArtistId), (13, fullTags.MusicBrainzAlbumId), (14, fullTags.MusicBrainzReleaseGroupId)])
        {
            full.Text(field, value!);
        }

        full.Number(15, 0xF0, fullTags.Year!.Value).Number(16, fullTags.TrackNumber!.Value).Number(17, fullTags.TrackTotal!.Value).Number(18, fullTags.DiscNumber!.Value).Number(19, fullTags.DiscTotal!.Value).Number(20, fullTags.Bpm!.Value)
            .Byte(21, 1)
            .ReplayGain(0x0F, fullTags.ReplayGain!.Value.TrackGain!.Value, fullTags.ReplayGain.Value.TrackPeak!.Value, fullTags.ReplayGain.Value.AlbumGain!.Value, fullTags.ReplayGain.Value.AlbumPeak!.Value)
            .Picture((byte)fullTags.Pictures[0].PictureType, fullTags.Pictures[0].MimeType!, fullTags.Pictures[0].Description!, fullTags.Pictures[0].Data);
        foreach (var (key, value) in fullTags.CustomFields)
        {
            full.Custom(key, value);
        }

        for (byte format = 0; format < Formats.Length; format++)
        {
            var name = Formats[format].ToString();
            yield return new FuzzSeed($"{name}-empty", (byte[])[format, 0]);
            yield return new FuzzSeed($"{name}-full", (byte[])[format, 0, .. full.ToArray()]);
            yield return new FuzzSeed($"{name}-edge-values", (byte[])[format, 0, .. edgeValues.ToArray()]);
            yield return new FuzzSeed($"{name}-edge-values-without-id3v1-or-padding", (byte[])[format, 0x03, .. edgeValues.ToArray()]);
            yield return new FuzzSeed($"{name}-limits", (byte[])[format, 0, .. limits.ToArray()]);
            yield return new FuzzSeed($"{name}-out-of-range-numbers", (byte[])[format, 0, .. outOfRange.ToArray()]);
            yield return new FuzzSeed($"{name}-custom-field-name-with-equals", (byte[])[format, 0, .. nameWithEquals.ToArray()]);
            yield return new FuzzSeed($"{name}-custom-field-name-with-null", (byte[])[format, 0, .. nameWithNull.ToArray()]);
            yield return new FuzzSeed($"{name}-picture-mime-type-with-null", (byte[])[format, 0, .. mimeTypeWithNull.ToArray()]);
            yield return new FuzzSeed($"{name}-picture-description-with-null", (byte[])[format, 0, .. descriptionWithNull.ToArray()]);
            yield return new FuzzSeed($"{name}-reserved-names", (byte[])[format, 0x08, .. reserved.ToArray()]);
        }
    }

    /// <summary>Builds the values part of an input of the write-values target, for the seeds.</summary>
    private sealed class ValueWriter
    {
        private readonly List<byte> _bytes = [];

        public byte[] ToArray() => [.. _bytes];

        public ValueWriter Text(int field, string value)
        {
            _bytes.Add((byte)field);
            AddText(value);
            return this;
        }

        public ValueWriter LongText(int field, string pattern, int kilobytes)
        {
            _bytes.Add((byte)field);
            AddLong(FuzzText.Encode(pattern), kilobytes);
            return this;
        }

        public ValueWriter Number(int field, int selector, int value = 0)
        {
            _bytes.Add((byte)field);
            _bytes.Add((byte)selector);
            if (selector is 0xF0)
            {
                _bytes.AddRange(BitConverter.GetBytes(value));
            }

            return this;
        }

        public ValueWriter Byte(int field, byte value)
        {
            _bytes.Add((byte)field);
            _bytes.Add(value);
            return this;
        }

        public ValueWriter ReplayGain(byte components, params double[] values)
        {
            _bytes.Add(22);
            _bytes.Add(components);
            foreach (var value in values)
            {
                _bytes.Add(0x80);
                _bytes.AddRange(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(value)));
            }

            return this;
        }

        public ValueWriter Picture(byte pictureType, string mimeType, string description, byte[] data)
        {
            _bytes.Add(23);
            _bytes.Add(pictureType);
            _bytes.Add(0);
            AddText(mimeType);
            AddText(description);
            AddBytes(data);
            return this;
        }

        public ValueWriter Custom(string key, string value)
        {
            _bytes.Add(24);
            AddText(key);
            AddText(value);
            return this;
        }

        public ValueWriter LongCustom(string key, string pattern, int kilobytes)
        {
            _bytes.Add(24);
            AddText(key);
            AddLong(FuzzText.Encode(pattern), kilobytes);
            return this;
        }

        private void AddText(string value) => AddBytes(FuzzText.Encode(value));

        private void AddBytes(byte[] value)
        {
            if (value.Length >= 0xC0)
                throw new ArgumentOutOfRangeException(nameof(value), value.Length, "A seed value is at most 191 bytes.");

            _bytes.Add((byte)value.Length);
            _bytes.AddRange(value);
        }

        private void AddLong(byte[] pattern, int kilobytes)
        {
            if (pattern.Length is 0 or > 16)
                throw new ArgumentOutOfRangeException(nameof(pattern), pattern.Length, "A seed pattern is 1 to 16 bytes.");

            _bytes.Add((byte)(0xC0 | (pattern.Length - 1)));
            _bytes.AddRange(pattern);
            _bytes.Add((byte)(kilobytes - 1));
        }
    }
}
