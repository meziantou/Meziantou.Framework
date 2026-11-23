using System.Buffers.Binary;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// The HUFF/CDIC compression of the text records of a book: a Huffman code (the HUFF record) whose symbols are the phrases
/// of a dictionary (the CDIC records). A phrase is text, or is itself compressed with the same code.
/// </summary>
internal sealed class HuffCdicDecoder
{
    // The nesting of the compressed phrases, and the total length of the decompressed phrases
    private const int MaxPhraseDepth = 32;
    private const int MaxExpandedPhrasesLength = MobiBook.MaxTextLength;

    private readonly CodeEntry[] _codes;
    private readonly ulong[] _minCodes;
    private readonly ulong[] _maxCodes;
    private readonly List<Phrase> _phrases;
    private long _expandedPhrasesLength;

    private HuffCdicDecoder(CodeEntry[] codes, ulong[] minCodes, ulong[] maxCodes, List<Phrase> phrases)
    {
        _codes = codes;
        _minCodes = minCodes;
        _maxCodes = maxCodes;
        _phrases = phrases;
    }

    /// <summary>Creates a decoder from the HUFF record and the CDIC records that follow it.</summary>
    /// <exception cref="InvalidDataException">The records are not valid.</exception>
    public static HuffCdicDecoder Create(IReadOnlyList<ReadOnlyMemory<byte>> records)
    {
        // A book can give no HUFF record (its number of records is 0)
        if (records.Count is 0)
            throw new InvalidDataException("The book has no HUFF record");

        var huff = records[0].Span;
        if (huff.Length < 16 || !huff.StartsWith("HUFF\0\0\0\x18"u8))
            throw new InvalidDataException("The HUFF record of the book is not valid");

        var codesOffset = BinaryPrimitives.ReadUInt32BigEndian(huff[8..]);
        var limitsOffset = BinaryPrimitives.ReadUInt32BigEndian(huff[12..]);
        if (codesOffset > huff.Length - (256 * 4) || limitsOffset > huff.Length - (64 * 4))
            throw new InvalidDataException("The HUFF record of the book is truncated");

        var codes = new CodeEntry[256];
        for (var i = 0; i < codes.Length; i++)
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(huff[((int)codesOffset + (i * 4))..]);
            var codeLength = (int)(value & 0x1F);
            var isTerminal = (value & 0x80) is not 0;
            if (codeLength is 0 || (codeLength <= 8 && !isTerminal))
                throw new InvalidDataException("The HUFF record of the book is not valid");

            codes[i] = new CodeEntry(codeLength, isTerminal, (((ulong)(value >> 8) + 1) << (32 - codeLength)) - 1);
        }

        // The smallest and the largest code of each length, from 1 to 32
        var minCodes = new ulong[33];
        var maxCodes = new ulong[33];
        maxCodes[0] = uint.MaxValue;
        for (var codeLength = 1; codeLength <= 32; codeLength++)
        {
            var position = (int)limitsOffset + ((codeLength - 1) * 8);
            minCodes[codeLength] = (ulong)BinaryPrimitives.ReadUInt32BigEndian(huff[position..]) << (32 - codeLength);
            maxCodes[codeLength] = (((ulong)BinaryPrimitives.ReadUInt32BigEndian(huff[(position + 4)..]) + 1) << (32 - codeLength)) - 1;
        }

        var phrases = new List<Phrase>();
        for (var i = 1; i < records.Count; i++)
        {
            ReadPhrases(records[i], phrases);
        }

        return new HuffCdicDecoder(codes, minCodes, maxCodes, phrases);
    }

    /// <summary>Decompresses a record and appends its text to the output.</summary>
    /// <exception cref="InvalidDataException">The compressed text is not valid, or the text is too long.</exception>
    public void Decompress(ReadOnlySpan<byte> input, MobiBuffer output) => Decompress(input, output, depth: 0);

    private static void ReadPhrases(ReadOnlyMemory<byte> record, List<Phrase> phrases)
    {
        var cdic = record.Span;
        if (cdic.Length < 16 || !cdic.StartsWith("CDIC\0\0\0\x10"u8))
            throw new InvalidDataException("A CDIC record of the book is not valid");

        var phraseCount = BinaryPrimitives.ReadUInt32BigEndian(cdic[8..]);
        var bits = BinaryPrimitives.ReadUInt32BigEndian(cdic[12..]);

        // Each record holds at most 2^bits phrases, and the last one the remaining phrases
        var remaining = Math.Max(0, phraseCount - (long)phrases.Count);
        var count = (int)Math.Min(bits >= 31 ? remaining : Math.Min(remaining, 1L << (int)bits), (cdic.Length - 16) / 2);
        for (var i = 0; i < count; i++)
        {
            var offset = 16 + BinaryPrimitives.ReadUInt16BigEndian(cdic[(16 + (i * 2))..]);
            if (offset > cdic.Length - 2)
                throw new InvalidDataException("A phrase of a CDIC record of the book is truncated");

            var header = BinaryPrimitives.ReadUInt16BigEndian(cdic[offset..]);
            var length = header & 0x7FFF;
            if (length > cdic.Length - offset - 2)
                throw new InvalidDataException("A phrase of a CDIC record of the book is truncated");

            // The high bit tells that the phrase is text, rather than compressed text
            phrases.Add(new Phrase(record.Slice(offset + 2, length), IsExpanded: (header & 0x8000) is not 0));
        }
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int position)
    {
        // The data is followed by zeros
        Span<byte> buffer = stackalloc byte[8];
        buffer.Clear();
        if (position < data.Length)
        {
            var available = data[position..];
            available[..Math.Min(8, available.Length)].CopyTo(buffer);
        }

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private void Decompress(ReadOnlySpan<byte> input, MobiBuffer output, int depth)
    {
        if (depth > MaxPhraseDepth)
            throw new InvalidDataException("The phrases of the HUFF/CDIC compression are nested too deeply");

        var bitsLeft = (long)input.Length * 8;
        var position = 0;
        var bits = ReadUInt64(input, position);
        var shift = 32;
        while (true)
        {
            if (shift <= 0)
            {
                position += 4;
                bits = ReadUInt64(input, position);
                shift += 32;
            }

            var code = (bits >> shift) & uint.MaxValue;
            var entry = _codes[code >> 24];
            var codeLength = entry.CodeLength;
            var maxCode = entry.MaxCode;
            if (!entry.IsTerminal)
            {
                while (code < _minCodes[codeLength])
                {
                    codeLength++;
                    if (codeLength > 32)
                        throw new InvalidDataException("The compressed text has a code that is not valid");
                }

                maxCode = _maxCodes[codeLength];
            }

            shift -= codeLength;
            bitsLeft -= codeLength;
            if (bitsLeft < 0)
                break;

            if (maxCode < code)
                throw new InvalidDataException("The compressed text has a code that is not valid");

            var index = (maxCode - code) >> (32 - codeLength);
            if (index >= (ulong)_phrases.Count)
                throw new InvalidDataException("The compressed text uses a phrase that the CDIC records do not define");

            output.Append(GetPhrase((int)index, depth).Span);
        }
    }

    private ReadOnlyMemory<byte> GetPhrase(int index, int depth)
    {
        var phrase = _phrases[index];
        if (phrase.IsExpanded)
            return phrase.Data;

        // A phrase that is being decompressed uses itself
        if (phrase.IsExpanding)
            throw new InvalidDataException("A phrase of the HUFF/CDIC compression uses itself");

        _phrases[index] = phrase with { IsExpanding = true };
        var buffer = new MobiBuffer((int)Math.Min(int.MaxValue, MaxExpandedPhrasesLength - _expandedPhrasesLength));
        Decompress(phrase.Data.Span, buffer, depth + 1);
        _expandedPhrasesLength += buffer.Length;

        var result = buffer.ToArray();
        _phrases[index] = new Phrase(result, IsExpanded: true);
        return result;
    }

    private readonly record struct CodeEntry(int CodeLength, bool IsTerminal, ulong MaxCode);

    private readonly record struct Phrase(ReadOnlyMemory<byte> Data, bool IsExpanded, bool IsExpanding = false);
}
