using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.MediaTags.Fuzz;

/// <summary>
/// <see cref="MediaFile"/> reports a bad file through its result and never throws for one. Reading does not depend on where
/// the file starts in the stream, nor on how many bytes each read returns. A successful write produces a file that reads
/// back, in the same format, with the tags it was given, and writing the tags read from it again changes none of them.
/// </summary>
public static class MediaTagsFuzzTargets
{
    private const string TestFilesPrefix = "TestFiles/";

    private static readonly MediaFormat[] Formats = Enum.GetValues<MediaFormat>();

    private static readonly string[] Tokens =
    [
        // Signatures
        "ID3", "TAG", "fLaC", "OggS", "OpusHead", "OpusTags", "\u0001vorbis", "\u0003vorbis", "\u0005vorbis", "RIFF", "RIFX", "RF64", "WAVE", "LIST", "INFO", "id3 ", "ID3 ", "data", "fmt ", "FORM", "AIFF", "AIFC", "COMM", "SSND",
        // MP4 atoms
        "ftyp", "moov", "mvhd", "trak", "mdia", "mdhd", "minf", "stbl", "stco", "co64", "udta", "meta", "hdlr", "mdir", "ilst", "mdat", "free", "skip", "moof", "trkn", "disk", "covr", "cpil", "tmpo", "gnre", "aART", "cprt", "----", "mean", "name", "com.apple.iTunes",
        // ID3v2 frames
        "TIT2", "TPE1", "TPE2", "TPE3", "TALB", "TCON", "TYER", "TDRC", "TRCK", "TPOS", "TCOM", "TCOP", "TSRC", "TBPM", "TLEN", "TCMP", "COMM", "USLT", "APIC", "TXXX", "UFID", "PRIV", "TT2", "TP1", "TAL", "PIC", "COM", "TXX",
        // MP3 frames
        "ÿû", "Xing", "Info", "VBRI", "LAME",
        // Vorbis comments
        "TITLE=", "ARTIST=", "ALBUM=", "ALBUMARTIST=", "GENRE=", "DATE=", "TRACKNUMBER=", "TRACKTOTAL=", "DISCNUMBER=", "COMPOSER=", "LYRICS=", "COMMENT=", "ISRC=", "BPM=", "COMPILATION=", "METADATA_BLOCK_PICTURE=",
        "REPLAYGAIN_TRACK_GAIN=", "REPLAYGAIN_TRACK_PEAK=", "REPLAYGAIN_ALBUM_GAIN=", "REPLAYGAIN_ALBUM_PEAK=", "MUSICBRAINZ_TRACKID=", "MUSICBRAINZ_ALBUMID=", "image/png", "image/jpeg", "-->",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("mediatags-read", Read) { Seeds = GetReadSeeds, Dictionary = Tokens },
        new("mediatags-write", Write) { Seeds = GetWriteSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// <c>format, layout, file</c>: <c>format</c> 0 detects the format, any other value forces one. <c>layout</c> gives the number
    /// of bytes before the file in the stream (low 4 bits) and the most bytes a read returns (high 4 bits).
    /// </summary>
    private static void Read(ReadOnlySpan<byte> data)
    {
        var formatSelector = FuzzInput.ReadByte(ref data);
        var layout = FuzzInput.ReadByte(ref data);
        var file = data.ToArray();
        var format = GetFormat(formatSelector);

        var detected = Detect(file, 0);
        var result = MediaFile.ReadTags(new MemoryStream(file, writable: false), format);
        if (result.IsSuccess)
        {
            FuzzAssert.Equal(format ?? detected, result.Value.Format, "The format of the tags is not the one that was read");
        }

        var expected = Describe(result);

        var prefixLength = layout & 0x0F;
        if (prefixLength > 0)
        {
            FuzzAssert.Equal(detected, Detect(file, prefixLength), $"The format detected after {prefixLength} other bytes differs");
            using var stream = new MemoryStream(Prefix(file, prefixLength), writable: false) { Position = prefixLength };
            FuzzAssert.Equal(expected, Describe(MediaFile.ReadTags(stream, format)), $"The tags read after {prefixLength} other bytes differ");
        }

        var maxBytesPerRead = (layout >> 4) + 1;
        using (var stream = new ShortReadStream(file, maxBytesPerRead))
        {
            FuzzAssert.Equal(expected, Describe(MediaFile.ReadTags(stream, format)), $"The tags read {maxBytesPerRead} byte(s) at a time differ");
        }
    }

    /// <summary>
    /// <c>format, mode, layout, file</c>: <c>format</c> 0 writes in the detected format, any other value forces one. <c>mode</c>
    /// gives the tags (low 2 bits: those read from the file, a full set, or none through <see cref="MediaFile.RemoveTags(Stream, Stream, MediaFormat)"/>)
    /// and the options (the other bits). <c>layout</c> is the one of the read target.
    /// </summary>
    private static void Write(ReadOnlySpan<byte> data)
    {
        var formatSelector = FuzzInput.ReadByte(ref data);
        var mode = FuzzInput.ReadByte(ref data);
        var layout = FuzzInput.ReadByte(ref data);
        var file = data.ToArray();

        MediaFormat format;
        if (GetFormat(formatSelector) is { } forcedFormat)
        {
            format = forcedFormat;
        }
        else if (Detect(file, 0) is { } detectedFormat)
        {
            format = detectedFormat;
        }
        else
        {
            return;
        }

        var tagsMode = (mode & 0x03) % 3;
        var remove = tagsMode is 2;
        MediaTagInfo tags;
        var hasReadTags = false;
        if (tagsMode is 0)
        {
            var read = MediaFile.ReadTags(new MemoryStream(file, writable: false), format);
            hasReadTags = read.IsSuccess;
            tags = read.IsSuccess ? read.Value : new MediaTagInfo();
        }
        else
        {
            tags = remove ? new MediaTagInfo() : CreateFullTags();
        }

        var options = new MediaTagWriteOptions
        {
            WriteId3v1Tag = (mode & 0x04) is 0,
            Id3v2PaddingSize = ((mode >> 3) & 0x03) switch
            {
                0 => 1024,
                1 => 0,
                2 => 1,
                _ => 4096,
            },
        };

        var output = new MemoryStream();
        var result = WriteTags(new MemoryStream(file, writable: false), output, tags, format, options, remove);

        // The same input, after other bytes in a stream that returns a few bytes per read, to an output that cannot seek
        var prefixLength = layout & 0x0F;
        var maxBytesPerRead = (layout >> 4) + 1;
        using var otherInput = new ShortReadStream(Prefix(file, prefixLength), maxBytesPerRead) { Position = prefixLength };
        using var otherOutput = new WriteOnlyStream();
        var otherResult = WriteTags(otherInput, otherOutput, tags, format, options, remove);
        FuzzAssert.Equal(Describe(result), Describe(otherResult), $"The result of the write after {prefixLength} other bytes, {maxBytesPerRead} byte(s) at a time, to an output that cannot seek, differs");
        if (!result.IsSuccess)
            return;

        var written = output.ToArray();
        FuzzAssert.True(written.AsSpan().SequenceEqual(otherOutput.ToArray()), $"The file written after {prefixLength} other bytes, {maxBytesPerRead} byte(s) at a time, to an output that cannot seek, differs");

        // An MP3 file is any file behind an ID3v2 tag, so the written tag can reveal another format behind it
        if (format is not MediaFormat.Mp3)
        {
            FuzzAssert.Equal<MediaFormat?>(format, Detect(written, 0), "The format detected in the written file differs");
        }

        var readBack = MediaFile.ReadTags(new MemoryStream(written, writable: false), format);
        FuzzAssert.True(readBack.IsSuccess, $"The written file does not read back: {readBack.Error} {readBack.ErrorMessage}");
        var readBackTags = readBack.Value;
        switch (tagsMode)
        {
            case 0 when hasReadTags:
                FuzzAssert.Equal(Describe(tags), Describe(readBackTags), "Writing the tags read from the file changes them");
                break;

            case 0:
                break;

            case 1:
                FuzzAssert.Equal(Describe(CreateFullTags(), CoreFields), Describe(readBackTags, CoreFields), "The written tags do not read back");
                break;

            default:
                FuzzAssert.Equal(Describe(new MediaTagInfo { Format = format, Duration = readBackTags.Duration }), Describe(readBackTags), "Removing the tags leaves some behind");
                break;
        }

        // The tags read from a written file are a fixed point
        var rewritten = new MemoryStream();
        var rewriteResult = MediaFile.WriteTags(new MemoryStream(written, writable: false), rewritten, readBackTags, format, options);
        FuzzAssert.True(rewriteResult.IsSuccess, $"The written file cannot be written again: {rewriteResult.Error} {rewriteResult.ErrorMessage}");
        var reread = MediaFile.ReadTags(new MemoryStream(rewritten.ToArray(), writable: false), format);
        FuzzAssert.True(reread.IsSuccess, $"The file written twice does not read back: {reread.Error} {reread.ErrorMessage}");
        FuzzAssert.Equal(Describe(readBackTags), Describe(reread.Value), "Writing the tags read from a written file changes them");
    }

    private static MediaTagResult WriteTags(Stream input, Stream output, MediaTagInfo tags, MediaFormat format, MediaTagWriteOptions options, bool remove)
        => remove ? MediaFile.RemoveTags(input, output, format) : MediaFile.WriteTags(input, output, tags, format, options);

    private static MediaFormat? GetFormat(byte selector) => selector is 0 ? null : Formats[(selector - 1) % Formats.Length];

    /// <summary>Detects the format of a file that starts after <paramref name="prefixLength"/> other bytes, and checks that the position is restored.</summary>
    private static MediaFormat? Detect(byte[] file, int prefixLength)
    {
        using var stream = new MemoryStream(Prefix(file, prefixLength), writable: false) { Position = prefixLength };
        var format = MediaFile.DetectFormat(stream);
        FuzzAssert.Equal((long)prefixLength, stream.Position, "DetectFormat does not restore the position of the stream");
        return format;
    }

    /// <summary>Puts bytes before the file. They are not an ID3v2 tag, so that the file does not look like an MP3 file to a parser that reads them.</summary>
    private static byte[] Prefix(byte[] file, int prefixLength)
    {
        if (prefixLength is 0)
            return file;

        var result = new byte[prefixLength + file.Length];
        result.AsSpan(0, prefixLength).Fill((byte)'x');
        file.CopyTo(result, prefixLength);
        return result;
    }

    private static MediaTagInfo CreateFullTags()
    {
        var tags = new MediaTagInfo
        {
            Title = "Fuzz Title é日本",
            Artist = "Fuzz Artist",
            Album = "Fuzz Album",
            AlbumArtist = "Fuzz Album Artist",
            Genre = "Rock",
            Year = 2024,
            TrackNumber = 3,
            TrackTotal = 12,
            DiscNumber = 1,
            DiscTotal = 2,
            Comment = "Fuzz Comment",
            Lyrics = "Line 1\nLine 2",
            Isrc = "USRC17607839",
            Composer = "Fuzz Composer",
            Conductor = "Fuzz Conductor",
            Copyright = "2024 Fuzz",
            Bpm = 120,
            IsCompilation = true,
            ReplayGain = new ReplayGainInfo { TrackGain = -6.5, TrackPeak = 0.98, AlbumGain = -7.25, AlbumPeak = 1 },
            MusicBrainzTrackId = "f1c4a1d2-9d4b-4e0a-8f3e-1c2b3a4d5e6f",
            MusicBrainzArtistId = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
            MusicBrainzAlbumId = "0a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9",
            MusicBrainzReleaseGroupId = "9f8e7d6c-5b4a-4392-8170-6f5e4d3c2b1a",
        };

        tags.Pictures.Add(new MediaPicture
        {
            PictureType = MediaPictureType.FrontCover,
            MimeType = "image/png",
            Description = "Cover",
            Data = Convert.FromHexString("89504E470D0A1A0A0000000D4948445200000001000000010802000000907753DE0000000C49444154789C63F80F00000101000518D84E0000000049454E44AE426082"),
        });
        tags.CustomFields["FUZZ"] = "Custom Value";
        return tags;
    }

    /// <summary>The fields that every format stores.</summary>
    private static readonly string[] CoreFields = [nameof(MediaTagInfo.Title), nameof(MediaTagInfo.Artist), nameof(MediaTagInfo.Album), nameof(MediaTagInfo.Year), nameof(MediaTagInfo.TrackNumber)];

    private static string Describe(MediaTagResult<MediaTagInfo> result) => result.IsSuccess ? Describe(result.Value) : "Error: " + result.Error;

    private static string Describe(MediaTagResult result) => result.IsSuccess ? "Success" : "Error: " + result.Error;

    private static string Describe(MediaTagInfo tags, IReadOnlyCollection<string>? fields = null)
    {
        var builder = new StringBuilder();
        Append(nameof(tags.Format), tags.Format);
        Append(nameof(tags.Title), tags.Title);
        Append(nameof(tags.Artist), tags.Artist);
        Append(nameof(tags.Album), tags.Album);
        Append(nameof(tags.AlbumArtist), tags.AlbumArtist);
        Append(nameof(tags.Genre), tags.Genre);
        Append(nameof(tags.Year), tags.Year);
        Append(nameof(tags.TrackNumber), tags.TrackNumber);
        Append(nameof(tags.TrackTotal), tags.TrackTotal);
        Append(nameof(tags.DiscNumber), tags.DiscNumber);
        Append(nameof(tags.DiscTotal), tags.DiscTotal);
        Append(nameof(tags.Comment), tags.Comment);
        Append(nameof(tags.Lyrics), tags.Lyrics);
        Append(nameof(tags.Isrc), tags.Isrc);
        Append(nameof(tags.Composer), tags.Composer);
        Append(nameof(tags.Conductor), tags.Conductor);
        Append(nameof(tags.Copyright), tags.Copyright);
        Append(nameof(tags.Bpm), tags.Bpm);
        Append(nameof(tags.Duration), tags.Duration?.Ticks);
        Append(nameof(tags.IsCompilation), tags.IsCompilation);
        Append(nameof(tags.ReplayGain), tags.ReplayGain is { } gain ? string.Create(CultureInfo.InvariantCulture, $"{gain.TrackGain:R} {gain.TrackPeak:R} {gain.AlbumGain:R} {gain.AlbumPeak:R}") : null);
        Append(nameof(tags.MusicBrainzTrackId), tags.MusicBrainzTrackId);
        Append(nameof(tags.MusicBrainzArtistId), tags.MusicBrainzArtistId);
        Append(nameof(tags.MusicBrainzAlbumId), tags.MusicBrainzAlbumId);
        Append(nameof(tags.MusicBrainzReleaseGroupId), tags.MusicBrainzReleaseGroupId);
        for (var i = 0; i < tags.Pictures.Count; i++)
        {
            var picture = tags.Pictures[i];
            Append(nameof(tags.Pictures), string.Create(CultureInfo.InvariantCulture, $"#{i} {picture.PictureType} {FuzzAssert.Format(picture.MimeType)} {FuzzAssert.Format(picture.Description)} {picture.Data.Length} bytes {Convert.ToHexString(SHA256.HashData(picture.Data))}"));
        }

        foreach (var (key, value) in tags.CustomFields.OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            Append(nameof(tags.CustomFields), FuzzAssert.Format(key) + " = " + FuzzAssert.Format(value));
        }

        return builder.ToString();

        void Append(string name, object? value)
        {
            if (fields is not null && !fields.Contains(name))
                return;

            builder.Append(name).Append(": ").Append(FuzzAssert.Format(value is IFormattable formattable ? formattable.ToString(format: null, CultureInfo.InvariantCulture) : value)).Append('\n');
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetTestFiles()
    {
        var assembly = typeof(MediaTagsFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(TestFilesPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (name[TestFilesPrefix.Length..], memory.ToArray());
        }
    }

    private static IEnumerable<FuzzSeed> GetReadSeeds()
    {
        foreach (var (name, data) in GetTestFiles())
        {
            yield return new FuzzSeed(name, (byte[])[0, 0, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetWriteSeeds()
    {
        foreach (var (name, data) in GetTestFiles())
        {
            for (byte mode = 0; mode < 3; mode++)
            {
                yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"{name}-{mode}"), (byte[])[0, mode, 0, .. data]);
            }
        }
    }
}
