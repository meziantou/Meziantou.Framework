using System.Buffers.Binary;
using System.Reflection;

namespace Meziantou.Framework;

internal static partial class UnicodeCharacterInfos
{
    private const string ResourceName = "Meziantou.Framework.Resources.UnicodeData.bin.gz";
    private static readonly Dictionary<Rune, UnicodeCharacterInfo> Infos = Create();

    public static bool TryGetInfo(Rune rune, out UnicodeCharacterInfo info)
    {
        return Infos.TryGetValue(rune, out info);
    }

    private static Dictionary<Rune, UnicodeCharacterInfo> Create()
    {
        using var stream = typeof(Unicode).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException("Cannot find Unicode data resource: " + ResourceName);

        using var decompressed = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        return ReadData(decompressed);
    }

    private static Dictionary<Rune, UnicodeCharacterInfo> ReadData(Stream stream)
    {
        var entryCount = Read7BitEncodedInt(stream);
        var stringCount = Read7BitEncodedInt(stream);

        var strings = new string[stringCount];
        for (var i = 0; i < strings.Length; i++)
        {
            strings[i] = ReadString255LengthMax(stream);
        }

        var entries = new Dictionary<Rune, UnicodeCharacterInfo>(capacity: entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var runeValue = ReadInt32(stream);
            var nameIndex = ReadInt32(stream);
            var category = (UnicodeCategory)ReadByte(stream);
            var bidiCategory = (UnicodeBidirectionalCategory)ReadByte(stream);
            var canonicalCombiningClass = ReadByte(stream);
            var decompositionIndex = ReadInt32(stream);
            var decimalDigitValue = ReadSByte(stream);
            var digitValue = ReadSByte(stream);
            var numericIndex = ReadInt32(stream);
            var mirrored = ReadByte(stream) != 0;
            var unicode1NameIndex = ReadInt32(stream);
            var isoCommentIndex = ReadInt32(stream);
            var simpleUppercaseMapping = ReadInt32(stream);
            var simpleLowercaseMapping = ReadInt32(stream);
            var simpleTitlecaseMapping = ReadInt32(stream);

            var info = new UnicodeCharacterInfo(
                rune: new Rune(runeValue),
                name: GetString(strings, nameIndex) ?? string.Empty,
                category: category,
                bidiCategory: bidiCategory,
                canonicalCombiningClass: canonicalCombiningClass,
                decompositionMapping: GetString(strings, decompositionIndex),
                decimalDigitValue: decimalDigitValue,
                digitValue: digitValue,
                numericValue: GetString(strings, numericIndex),
                mirrored: mirrored,
                unicode1Name: GetString(strings, unicode1NameIndex),
                isoComment: GetString(strings, isoCommentIndex),
                simpleUppercaseMapping: simpleUppercaseMapping,
                simpleLowercaseMapping: simpleLowercaseMapping,
                simpleTitlecaseMapping: simpleTitlecaseMapping);

            entries.TryAdd(info.Rune, info);
        }

        return entries;
    }

    private static string? GetString(string[] values, int index)
    {
        if (index < 0)
            return null;

        return values[index];
    }

    private static string ReadString255LengthMax(Stream stream)
    {
        var length = ReadByte(stream);
        if (length == 0)
            return "";

        Span<byte> buffer = stackalloc byte[MaxCharacterNameLength];
        stream.ReadExactly(buffer[..length]);
        return Encoding.UTF8.GetString(buffer[..length]);
    }

    private static int Read7BitEncodedInt(Stream stream)
    {
        var count = 0;
        var shift = 0;
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
                throw new EndOfStreamException();

            count |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;

            shift += 7;
            if (shift >= 35)
                throw new InvalidDataException("Invalid 7-bit encoded integer.");
        }

        return count;
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static byte ReadByte(Stream stream)
    {
        var value = stream.ReadByte();
        if (value < 0)
            throw new EndOfStreamException();

        return (byte)value;
    }

    private static sbyte ReadSByte(Stream stream)
    {
        var value = stream.ReadByte();
        if (value < 0)
            throw new EndOfStreamException();

        return unchecked((sbyte)value);
    }
}
