using System.Globalization;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.MediaTags.Fuzz;

/// <summary>
/// Writes arbitrary native fields to the test files of a format. Writing either fails with <see cref="MediaTagError.InvalidTagData"/>,
/// or produces a file whose native fields read back as the fields of the tag type of the format that were written, and are a
/// fixed point.
/// </summary>
/// <remarks>
/// Some fields do not read back exactly. An ID3v2 frame whose flags describe its stored content (compression, unsynchronisation,
/// ...) reads back decoded when it can be, and a described <c>COMM</c> or <c>USLT</c> frame is the comment or the lyrics when the
/// tag has none. The name of an ASF attribute is UTF-16, which loses its trailing null characters and replaces its unpaired
/// surrogates. Only the fixed point is checked for them.
/// </remarks>
public static partial class MediaTagsFuzzTargets
{
    private static readonly MediaTagType[] TagTypes = Enum.GetValues<MediaTagType>();

    /// <summary>
    /// <c>format, file, fields</c>: <c>format</c> selects the format and <c>file</c> one of its test files. Each field is a byte
    /// that selects the tag type, the identifier (<see cref="ReadValueText"/>), the flags as a little-endian 16-bit value
    /// (or 32-bit when the byte that precedes them is 0xFF), and the data.
    /// </summary>
    private static void NativeFields(ReadOnlySpan<byte> data)
    {
        var format = Formats[FuzzInput.ReadByte(ref data) % Formats.Length];
        var files = ValueTestFiles.Where(file => file.Format == format).ToList();
        if (files.Count is 0)
            return;

        var (fileName, file, _) = files[FuzzInput.ReadByte(ref data) % files.Count];
        var budget = MaxLongValueBudget;
        var fields = new List<MediaNativeField>();
        while (!data.IsEmpty)
        {
            var fieldTagType = TagTypes[FuzzInput.ReadByte(ref data) % TagTypes.Length];
            var id = ReadValueText(ref data, ref budget);
            var wide = FuzzInput.ReadByte(ref data) is 0xFF;
            var flags = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
            if (wide)
            {
                flags |= (FuzzInput.ReadByte(ref data) << 16) | (FuzzInput.ReadByte(ref data) << 24);
            }

            fields.Add(new MediaNativeField { TagType = fieldTagType, Id = id, Flags = flags, Data = ReadValueBytes(ref data, ref budget) });
        }

        var output = new MemoryStream();
        var result = MediaFile.WriteTags(new MemoryStream(file, writable: false), output, new MediaTagInfo { NativeFields = fields }, format);
        if (!result.IsSuccess)
        {
            FuzzAssert.Equal(MediaTagError.InvalidTagData, result.Error, $"{fileName}: the write of native fields fails with another error: {result.ErrorMessage}");
            return;
        }

        var readBack = MediaFile.ReadTags(new MemoryStream(output.ToArray(), writable: false), format);
        FuzzAssert.True(readBack.IsSuccess, $"{fileName}: the written file does not read back: {readBack.Error} {readBack.ErrorMessage}");
        var readBackFields = readBack.Value.NativeFields;
        FuzzAssert.True(readBackFields is not null, $"{fileName}: reading does not set the native fields");

        var tagType = GetNativeTagType(format);
        var written = fields.Where(field => field.TagType == tagType).ToList();
        if (written.All(field => ReadsBackExactly(field)))
        {
            FuzzAssert.Equal(DescribeNativeFields(written), DescribeNativeFields(readBackFields), $"{fileName}: the native fields do not read back");
        }

        // The tags read from the written file are a fixed point
        var rewritten = new MemoryStream();
        var rewriteResult = MediaFile.WriteTags(new MemoryStream(output.ToArray(), writable: false), rewritten, readBack.Value, format);
        FuzzAssert.True(rewriteResult.IsSuccess, $"{fileName}: the written file cannot be written again: {rewriteResult.Error} {rewriteResult.ErrorMessage}");
        var reread = MediaFile.ReadTags(new MemoryStream(rewritten.ToArray(), writable: false), format);
        FuzzAssert.True(reread.IsSuccess, $"{fileName}: the file written twice does not read back: {reread.Error} {reread.ErrorMessage}");
        FuzzAssert.Equal(Describe(readBack.Value), Describe(reread.Value), $"{fileName}: writing the tags read from the written file changes them");
    }

    private static MediaTagType? GetNativeTagType(MediaFormat format) => GetFamily(format) switch
    {
        TagFamily.Id3v2 => MediaTagType.Id3v2,
        TagFamily.Mp4 => MediaTagType.Mp4,
        TagFamily.Ape => MediaTagType.Ape,
        TagFamily.Asf => MediaTagType.Asf,
        _ => null,
    };

    private static bool ReadsBackExactly(MediaNativeField field)
    {
        if (field.TagType is MediaTagType.Asf)
            return field.Id == Utf16(field.Id).TrimEnd('\0');

        if (field.TagType is not MediaTagType.Id3v2)
            return true;

        // Only the status flags, or an encrypted frame, which is copied as stored
        return field.Id is not ("COMM" or "USLT") && ((field.Flags & ~0x3000) is 0 || (field.Flags & 0x0004) is not 0);
    }

    private static string DescribeNativeFields(IEnumerable<MediaNativeField> fields) => string.Join('\n', fields.Select(DescribeNativeField));

    private static IEnumerable<FuzzSeed> GetNativeFieldsSeeds()
    {
        var id3v2 = new NativeFieldWriter()
            .Field(MediaTagType.Id3v2, "PRIV", 0, [.. "owner"u8, 0, 1, 2, 3])
            .Field(MediaTagType.Id3v2, "TPUB", 0x1000, [3, .. "Label"u8])
            .Field(MediaTagType.Id3v2, "COMM", 0, [3, (byte)'e', (byte)'n', (byte)'g', .. "iTunNORM"u8, 0, .. "value"u8])
            .Field(MediaTagType.Id3v2, "AENC", 0x0004, [1, 2, 3])
            .Field(MediaTagType.Mp4, "pgap", 0, [1]);
        var mp4 = new NativeFieldWriter()
            .Field(MediaTagType.Mp4, "pgap", 0, [0, 0, 0, 9, .. "data"u8, 0, 0, 0, 21, 0, 0, 0, 0, 1])
            .Field(MediaTagType.Mp4, "\u00A9too", 0, [0, 0, 0, 16, .. "data"u8, 0, 0, 0, 1, 0, 0, 0, 0, .. "Tool"u8])
            .Field(MediaTagType.Mp4, "free", 0, []);
        var ape = new NativeFieldWriter()
            .Field(MediaTagType.Ape, "Private Data", 2, [0, 1, 2, 3])
            .Field(MediaTagType.Ape, "Link", 5, "https://example.com"u8.ToArray());
        var asf = new NativeFieldWriter()
            .Field(MediaTagType.Asf, "WM/EncodingTime", 4, BitConverter.GetBytes(132_000_000_000_000_000L))
            .Field(MediaTagType.Asf, "WM/MediaIsLive", 2, [1, 0, 0, 0])
            .Field(MediaTagType.Asf, "WM/WMCollectionID", 6, new byte[16])
            .Field(MediaTagType.Asf, "Binary", 1, [1, 2, 3]);
        var invalid = new NativeFieldWriter()
            .Field(MediaTagType.Id3v2, "TIT2", 0, [3, (byte)'x'])
            .Field(MediaTagType.Mp4, "moov", 0, [])
            .Field(MediaTagType.Ape, "Cover Art (Front)", 2, [0, 1])
            .Field(MediaTagType.Asf, "Text", 0, [0, 0]);

        for (byte format = 0; format < Formats.Length; format++)
        {
            var name = Formats[format].ToString();
            var fileCount = ValueTestFiles.Count(file => file.Format == Formats[format]);
            for (byte file = 0; file < fileCount; file++)
            {
                yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"{name}-{file}-empty"), (byte[])[format, file]);
            }

            yield return new FuzzSeed($"{name}-id3v2", (byte[])[format, 0, .. id3v2.ToArray()]);
            yield return new FuzzSeed($"{name}-mp4", (byte[])[format, 0, .. mp4.ToArray()]);
            yield return new FuzzSeed($"{name}-ape", (byte[])[format, 0, .. ape.ToArray()]);
            yield return new FuzzSeed($"{name}-asf", (byte[])[format, 0, .. asf.ToArray()]);
            yield return new FuzzSeed($"{name}-invalid", (byte[])[format, 0, .. invalid.ToArray()]);
        }
    }

    /// <summary>Builds the fields part of an input of the native-fields target, for the seeds.</summary>
    private sealed class NativeFieldWriter
    {
        private readonly List<byte> _bytes = [];

        public byte[] ToArray() => [.. _bytes];

        public NativeFieldWriter Field(MediaTagType tagType, string id, int flags, byte[] data)
        {
            _bytes.Add((byte)Array.IndexOf(TagTypes, tagType));
            AddBytes(FuzzText.Encode(id));
            _bytes.Add(0);
            _bytes.Add((byte)flags);
            _bytes.Add((byte)(flags >> 8));
            AddBytes(data);
            return this;
        }

        private void AddBytes(byte[] value)
        {
            if (value.Length >= 0xC0)
                throw new ArgumentOutOfRangeException(nameof(value), value.Length, "A seed value is at most 191 bytes.");

            _bytes.Add((byte)value.Length);
            _bytes.AddRange(value);
        }
    }
}
