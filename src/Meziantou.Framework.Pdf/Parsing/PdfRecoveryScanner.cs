using System.Buffers;
using System.Runtime.InteropServices;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Finds the objects (<c>12 0 obj</c>) and the trailers of a file by scanning its bytes, for files whose
/// cross-reference information is missing or wrong. The last definition of an object wins, as in an incremental update.
/// </summary>
/// <remarks>
/// <para>
/// The bytes of a stream whose length is reliable (a <c>/Length</c> followed by <c>endstream</c>) are not scanned: an
/// object or a trailer inside them, such as the objects of an embedded PDF, is data, not a definition of the file.
/// </para>
/// <para>
/// The starts of the objects and the trailers found are the boundaries of the objects: the recovery parses an object
/// up to the next boundary at most, so that a damaged object (an unterminated string, a stream without
/// <c>endstream</c>) cannot make every object read the rest of the file.
/// </para>
/// </remarks>
internal sealed class PdfRecoveryScanner
{
    private const int ChunkSize = 4 * 1024 * 1024;

    // The context read before a keyword found at the start of a chunk: the object number and generation before "obj"
    private const int Overlap = 256;

    private const int MaxDictionaryWindow = 64 * 1024;
    private const int MaxWhitespaceAfterStream = 256;

    private readonly HashSet<int> _ambiguousGenerations = [];
    private long[] _boundaries = [];
    private long _length;

    private PdfRecoveryScanner()
    {
    }

    public Dictionary<int, (long Offset, int Generation)> Offsets { get; } = [];

    /// <summary>Gets the offsets that follow the <c>trailer</c> keywords, in the order of the file.</summary>
    public List<long> TrailerOffsets { get; } = [];

    /// <summary>Gets the number of object definitions skipped because they are in the data of a stream.</summary>
    public int SkippedDefinitions { get; private set; }

    public static PdfRecoveryScanner Scan(ReadOnlyMemory<byte> data, int maxDepth = PdfReadOptions.DefaultMaxNestingDepth) => Scan(new MemoryPdfInput(data), maxDepth);

    public static PdfRecoveryScanner Scan(PdfInput input, int maxDepth)
    {
        var scanner = new PdfRecoveryScanner { _length = input.Length };
        var headers = new List<Header>();
        var trailers = new List<long>();
        if (input is MemoryPdfInput memory)
        {
            FindKeywords(memory.Data.Span, 0, 0, memory.Data.Length, headers, trailers);
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize + Overlap);
            try
            {
                long processed = 0;
                while (processed < input.Length)
                {
                    var chunkStart = Math.Max(0, processed - Overlap);
                    var read = input.Read(chunkStart, buffer.AsSpan(0, (int)Math.Min(ChunkSize + Overlap, input.Length - chunkStart)));
                    if (read <= 0)
                        break;

                    var chunkEnd = chunkStart + read;
                    var isLast = chunkEnd >= input.Length;

                    // A keyword is processed in the chunk that holds it and the byte after it ("trailer" is the longest)
                    var limit = isLast ? chunkEnd : chunkEnd - "trailer".Length - 1;
                    FindKeywords(buffer.AsSpan(0, read), chunkStart, processed, limit, headers, trailers);
                    processed = limit;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        scanner.Build(input, headers, trailers, maxDepth);
        return scanner;
    }

    /// <summary>Indicates whether the scan found one generation of an object only: a reference with another generation can only mean it.</summary>
    public bool HasSingleGeneration(int number) => !_ambiguousGenerations.Contains(number);

    /// <summary>Gets the first boundary (the start of an object or a trailer) after an offset, or the length of the file.</summary>
    public long GetBoundary(long offset)
    {
        var index = Array.BinarySearch(_boundaries, offset + 1);
        if (index < 0)
        {
            index = ~index;
        }

        return index < _boundaries.Length ? _boundaries[index] : _length;
    }

    private static void FindKeywords(ReadOnlySpan<byte> span, long chunkStart, long from, long to, List<Header> headers, List<long> trailers)
    {
        var start = (int)(from - chunkStart);
        var end = (int)(to - chunkStart);
        var position = start;
        while (position < end)
        {
            var index = span[position..].IndexOf("obj"u8);
            if (index < 0 || position + index >= end)
                break;

            var found = position + index;
            position = found + 3;
            if (position < span.Length && PdfLexer.IsRegular(span[position]))
                continue;

            if (TryReadHeader(span, found, out var number, out var generation, out var headerStart))
            {
                headers.Add(new Header(chunkStart + headerStart, number, generation));
            }
        }

        position = start;
        while (position < end)
        {
            var index = span[position..].IndexOf("trailer"u8);
            if (index < 0 || position + index >= end)
                break;

            var found = position + index;
            position = found + 7;
            if ((chunkStart + found == 0 || !PdfLexer.IsRegular(span[found - 1])) && (position >= span.Length || !PdfLexer.IsRegular(span[position])))
            {
                trailers.Add(chunkStart + found);
            }
        }
    }

    private void Build(PdfInput input, List<Header> headers, List<long> trailers, int maxDepth)
    {
        // The last definition of each number, to resolve the indirect lengths of the streams
        var last = new Dictionary<int, long>();
        foreach (var header in headers)
        {
            last[header.Number] = header.Start;
        }

        var parsingObjects = new PdfIndirectObjectCollection();
        var skipped = new List<(long Start, long End)>();
        var kept = new List<long>(headers.Count + trailers.Count);
        var generations = new Dictionary<int, int>();
        long skipUntil = -1;
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (header.Start < skipUntil)
            {
                SkippedDefinitions++;
                continue;
            }

            var next = i + 1 < headers.Count ? headers[i + 1].Start : input.Length;
            if (TryGetStreamEnd(input, header.Start, next, last, parsingObjects, maxDepth) is { } streamEnd)
            {
                skipUntil = streamEnd;
                skipped.Add((header.Start, streamEnd));
            }

            kept.Add(header.Start);
            Offsets[header.Number] = (header.Start, header.Generation);
            if (!generations.TryAdd(header.Number, header.Generation) && generations[header.Number] != header.Generation)
            {
                _ambiguousGenerations.Add(header.Number);
            }
        }

        var range = 0;
        foreach (var trailer in trailers)
        {
            while (range < skipped.Count && skipped[range].End <= trailer)
            {
                range++;
            }

            if (range < skipped.Count && skipped[range].Start < trailer)
                continue;

            kept.Add(trailer);
            TrailerOffsets.Add(trailer + "trailer".Length);
        }

        kept.Sort();
        _boundaries = [.. kept];
    }

    /// <summary>
    /// Reads the dictionary of the object at <paramref name="start"/>: when it is a stream whose <c>/Length</c> ends at an
    /// <c>endstream</c> keyword, gets the offset after the keyword.
    /// </summary>
    private static long? TryGetStreamEnd(PdfInput input, long start, long next, Dictionary<int, long> last, PdfIndirectObjectCollection objects, int maxDepth)
    {
        var windowLength = (int)Math.Min(next - start, MaxDictionaryWindow);
        if (windowLength <= 0)
            return null;

        byte[]? rented = null;
        try
        {
            ReadOnlyMemory<byte> window;
            if (input.IsInMemory)
            {
                window = input.GetMemory(start, windowLength);
            }
            else
            {
                rented = ArrayPool<byte>.Shared.Rent(windowLength);
                window = rented.AsMemory(0, input.Read(start, rented.AsSpan(0, windowLength)));
            }

            var lexer = new PdfLexer(window);
            lexer.Next();
            lexer.Next();
            lexer.Next();
            var token = lexer.Next();
            if (token.Kind != PdfTokenKind.DictionaryStart)
                return null;

            var parser = new PdfObjectParser(lexer, objects, maxDepth);
            if (parser.ParseValue(token, depth: 0) is not PdfDictionary dictionary || !lexer.IsKeyword(lexer.Next(), "stream"u8))
                return null;

            var length = dictionary[PdfNames.Length] switch
            {
                PdfInteger integer => integer.Value,
                PdfReference reference when last.TryGetValue(reference.ObjectNumber, out var offset) => ReadIntegerObject(input, offset),
                _ => -1,
            };
            if (length < 0)
                return null;

            var dataStart = start + PdfObjectParser.GetStreamDataStart(window.Span, lexer.Position);
            if (dataStart > input.Length - length)
                return null;

            var dataEnd = dataStart + length;
            Span<byte> after = stackalloc byte[MaxWhitespaceAfterStream + 9];
            after = after[..input.Read(dataEnd, after)];
            var whitespace = 0;
            while (whitespace < after.Length && PdfLexer.IsWhitespace(after[whitespace]))
            {
                whitespace++;
            }

            if (!after[whitespace..].StartsWith("endstream"u8))
                return null;

            return dataEnd + whitespace + "endstream".Length;
        }
        catch (PdfException)
        {
            return null;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Reads an object that is an integer (<c>12 0 obj 345 endobj</c>), the indirect length of a stream.</summary>
    private static long ReadIntegerObject(PdfInput input, long offset)
    {
        var buffer = new byte[64];
        var read = input.Read(offset, buffer);
        var lexer = new PdfLexer(buffer.AsMemory(0, read));
        if (lexer.Next().Kind != PdfTokenKind.Integer || lexer.Next().Kind != PdfTokenKind.Integer || !lexer.IsKeyword(lexer.Next(), "obj"u8))
            return -1;

        // The integer is complete when a byte follows it, or when the file ends
        var value = lexer.Next();
        return value.Kind == PdfTokenKind.Integer && (value.End < read || read < buffer.Length) ? value.IntegerValue : -1;
    }

    /// <summary>Reads <c>number generation</c> backwards from the <c>obj</c> keyword at <paramref name="objIndex"/>.</summary>
    private static bool TryReadHeader(ReadOnlySpan<byte> span, int objIndex, out int number, out int generation, out int start)
    {
        number = 0;
        generation = 0;
        start = 0;
        var position = objIndex - 1;
        if (position < 0 || !PdfLexer.IsWhitespace(span[position]))
            return false;

        while (position >= 0 && PdfLexer.IsWhitespace(span[position]))
        {
            position--;
        }

        if (!TryReadDigitsBackwards(span, ref position, out var generationValue))
            return false;

        if (position < 0 || !PdfLexer.IsWhitespace(span[position]))
            return false;

        while (position >= 0 && PdfLexer.IsWhitespace(span[position]))
        {
            position--;
        }

        if (!TryReadDigitsBackwards(span, ref position, out var numberValue))
            return false;

        // The number starts a token
        if (position >= 0 && PdfLexer.IsRegular(span[position]))
            return false;

        if (numberValue is <= 0 or > int.MaxValue || generationValue > ushort.MaxValue)
            return false;

        number = (int)numberValue;
        generation = (int)generationValue;
        start = position + 1;
        return true;
    }

    private static bool TryReadDigitsBackwards(ReadOnlySpan<byte> span, ref int position, out long value)
    {
        value = 0;
        var end = position;
        while (position >= 0 && span[position] is >= (byte)'0' and <= (byte)'9' && end - position < 10)
        {
            position--;
        }

        if (position == end)
            return false;

        for (var i = position + 1; i <= end; i++)
        {
            value = (value * 10) + (span[i] - '0');
        }

        return true;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Header(long Start, int Number, int Generation);
}
