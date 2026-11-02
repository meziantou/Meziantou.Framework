using System.Buffers;
using System.Text;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Security;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Reads the structure of a file (ISO 32000-2, 7.5): the header, the chain of cross-reference sections (tables, streams,
/// hybrid files, incremental updates), and the indirect objects when they are first resolved. When the cross-reference
/// information is missing or wrong, the objects are found by scanning the file (<see cref="PdfRecoveryScanner"/>).
/// </summary>
/// <remarks>
/// The file is an array in memory or a seekable stream read on demand (<see cref="PdfInput"/>). The objects of a stream
/// are parsed in a window of the file, made larger when an object does not fit in it; the data of a stream after the
/// window is read when it is first used.
/// </remarks>
internal sealed class PdfFileReader : IPdfObjectLoader
{
    private const int MaxXrefSections = 4096;
    private const int MaxCachedObjectStreams = 64;
    private const long MaxCachedObjectStreamBytes = 64L * 1024 * 1024;
    private const int InitialWindowSize = 4096;
    private const int StartXrefSearchChunk = 64 * 1024;
    private const int MaxCandidateLength = 64 * 1024;

    private static readonly PdfName[] TrailerKeys = [PdfNames.Size, PdfNames.Root, PdfNames.Info, PdfNames.ID, PdfNames.Encrypt];

    private readonly PdfInput _input;
    private readonly PdfReadOptions _options;
    private readonly ObjectStreamCache _objectStreams = new(MaxCachedObjectStreams, MaxCachedObjectStreamBytes);
    private readonly PdfNameCache _names = new();
    private PdfRecoveryScanner? _scanner;
    private int _encryptObjectNumber = -1;

    private PdfFileReader(PdfInput input, PdfReadOptions options, PdfIndirectObjectCollection objects)
    {
        _input = input;
        _options = options;
        Objects = objects;
        DecodeLimits = new PdfDecodeLimits(options.MaxDecodedStreamLength);
    }

    private delegate T ParseFunc<out T>(PdfObjectParser parser);

    [Flags]
    private enum CandidateKind
    {
        None = 0,
        ObjectStream = 1,
        CrossReferenceStream = 2,
        Catalog = 4,
        All = ObjectStream | CrossReferenceStream | Catalog,
    }

    public PdfIndirectObjectCollection Objects { get; }

    public PdfDecodeLimits DecodeLimits { get; }

    public PdfVersion HeaderVersion { get; private set; } = PdfVersion.Pdf14;

    /// <summary>Gets the offset of the header: the offsets of some files count from it, not from the start of the file.</summary>
    public long HeaderOffset { get; private set; }

    public PdfDictionary Trailer { get; private set; } = new();

    /// <summary>Gets a value indicating whether the cross-reference information was rebuilt by scanning the file.</summary>
    public bool IsRepaired { get; private set; }

    /// <summary>Gets the offset of the last cross-reference section, the <c>/Prev</c> of an incremental update.</summary>
    public long LastXrefOffset { get; private set; } = -1;

    /// <summary>Gets a value indicating whether the last cross-reference section is a stream.</summary>
    public bool LastXrefIsStream { get; private set; }

    /// <summary>Gets the bytes of the file.</summary>
    public PdfInput Input => _input;

    /// <summary>Gets the options the file was read with, among them the password of an encrypted file.</summary>
    public PdfReadOptions Options => _options;

    /// <summary>Gets the security handler of an encrypted file, which decrypts the objects when they are loaded.</summary>
    public SecurityHandler? Security { get; private set; }

    public static PdfFileReader Open(ReadOnlyMemory<byte> data, PdfReadOptions options) => Open(new MemoryPdfInput(data), options);

    public static PdfFileReader Open(PdfInput input, PdfReadOptions options)
    {
        var objects = new PdfIndirectObjectCollection();
        var reader = new PdfFileReader(input, options, objects);
        objects.Loader = reader;
        reader.ReadHeader();
        if (reader.TryReadCrossReferences())
        {
            // The objects are decrypted from the first one that is loaded after the encryption dictionary
            reader.InitializeSecurity();
            if (reader.Trailer.GetResolved(PdfNames.Root) is not PdfDictionary)
            {
                reader.Repair(reader.Trailer);
            }
        }
        else
        {
            reader.Repair(previousTrailer: null);
        }

        if (reader.Trailer.GetResolved(PdfNames.Root) is not PdfDictionary)
            throw new InvalidPdfContentException("The document has no catalog (the /Root of the trailer)");

        return reader;
    }

    public PdfObject Load(IndirectObjectEntry entry)
    {
        var number = entry.Reference.ObjectNumber;
        if (entry.ContainerNumber > 0)
        {
            var value = TryLoadCompressed(entry.ContainerNumber, entry.IndexInContainer, number);
            if (value is not null)
                return value;
        }
        else if (entry.Offset >= 0)
        {
            var value = TryLoadAt(entry.Offset, number, entry.Reference.Generation);
            if (value is null && HeaderOffset > 0)
            {
                value = TryLoadAt(entry.Offset + HeaderOffset, number, entry.Reference.Generation);
            }

            if (value is not null)
                return value;
        }

        // The cross-reference entry is wrong: look for the object in the file
        var scanner = GetScanner();
        if (scanner.Offsets.TryGetValue(number, out var found) && found.Offset != entry.Offset)
        {
            var value = TryLoadAt(found.Offset, number, generation: null);
            if (value is not null)
                return value;
        }

        return PdfNull.Instance;
    }

    public bool AcceptsOtherGeneration(int objectNumber) => IsRepaired && _scanner is not null && _scanner.HasSingleGeneration(objectNumber);

    private PdfRecoveryScanner GetScanner() => _scanner ??= PdfRecoveryScanner.Scan(_input, _options.MaxNestingDepth);

    /// <summary>Authenticates the password of an encrypted file (7.6): the encryption dictionary is not encrypted itself.</summary>
    private void InitializeSecurity()
    {
        if (Security is not null || Trailer[PdfNames.Encrypt] is not { } encrypt || encrypt is PdfNull)
            return;

        if (encrypt is PdfReference reference)
        {
            _encryptObjectNumber = reference.ObjectNumber;
        }

        if (PdfReference.ResolveIfReference(encrypt) is not PdfDictionary dictionary)
            throw new InvalidPdfContentException("The /Encrypt entry of the trailer is not a dictionary");

        var id = Trailer.GetResolved(PdfNames.ID) is PdfArray ids && ids.TryGetResolved<PdfString>(0, out var first) ? first.Span : [];
        Security = SecurityHandler.Open(dictionary, id, _options);
    }

    private void ReadHeader()
    {
        Span<byte> start = stackalloc byte[1024];
        start = start[.._input.Read(0, start)];
        long index = start.IndexOf("%PDF-"u8);
        if (index < 0)
        {
            // Some producers prepend garbage beyond 1 KiB, and some files have no header at all: like other readers, the
            // file is read anyway, and it is rejected when no catalog can be found
            index = IndexOf(_input, "%PDF-"u8);
            if (index < 0)
                return;
        }

        HeaderOffset = index;
        Span<byte> version = stackalloc byte[3];
        if (_input.Read(index + 5, version) == 3 && PdfVersion.TryParse(Encoding.ASCII.GetString(version), out var parsed))
        {
            HeaderVersion = parsed;
        }
    }

    private bool TryReadCrossReferences()
    {
        var startxref = LastIndexOfKeyword(_input, "startxref"u8);
        if (startxref < 0)
            return false;

        var offsetToken = Parse(startxref + "startxref".Length, _input.Length, static parser => parser.Lexer.Next());
        if (offsetToken.Kind != PdfTokenKind.Integer || offsetToken.IntegerValue < 0 || offsetToken.IntegerValue >= _input.Length)
            return false;

        var entries = new Dictionary<int, XrefEntry>();
        PdfDictionary? trailer = null;
        var visited = new HashSet<long>();
        long offset = offsetToken.IntegerValue;
        try
        {
            while (offset >= 0)
            {
                if (!visited.Add(offset) || visited.Count > MaxXrefSections)
                    break;

                var section = ReadSection(offset);
                if (section is null)
                {
                    if (trailer is null)
                        return false;

                    // A broken /Prev: keep what the newer sections give, the scan fills the rest when needed
                    break;
                }

                if (trailer is null)
                {
                    trailer = section.Trailer;
                    LastXrefOffset = offset;
                    LastXrefIsStream = section.IsStream;
                }
                else
                {
                    // The newest trailer wins; an update that omits an entry inherits it. An entry whose value is null is
                    // equivalent to an absent entry (ISO 32000-2, 7.3.7): it inherits the entry of the older trailer too
                    foreach (var key in TrailerKeys)
                    {
                        if (trailer[key] is null or PdfNull && section.Trailer[key] is { } value and not PdfNull)
                        {
                            trailer[key] = value;
                        }
                    }
                }

                foreach (var entry in section.Entries)
                {
                    entries.TryAdd(entry.Key, entry.Value);
                }

                offset = section.Trailer.GetResolved(PdfNames.Prev) is PdfNumber prev ? (long)prev.DoubleValue : -1;
            }
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
        {
            // A cross-reference stream with a filter that is not supported is read like a damaged one
            if (trailer is null)
                return false;
        }

        if (trailer is null)
            return false;

        Trailer = StripXrefKeys(trailer);
        RegisterEntries(entries);
        // The numbers below /Size are not given to new objects, even when the file does not use them. A bogus /Size
        // (2147483647) would leave no number to give: the numbers the file uses are registered, so the numbers that
        // are not are kept below the length of the file
        Objects.Reserve((int)Math.Clamp(Trailer.GetInteger(PdfNames.Size, 0) - 1, 0, Math.Min(_input.Length, PdfIndirectObjectCollection.MaxAllocatableObjectNumber)));
        return true;
    }

    private XrefSection? ReadSection(long offset)
    {
        var section = TryReadSection(offset);
        if (section is null && HeaderOffset > 0 && offset + HeaderOffset < _input.Length)
        {
            section = TryReadSection(offset + HeaderOffset);
        }

        return section;
    }

    private XrefSection? TryReadSection(long offset)
    {
        if (offset < 0 || offset >= _input.Length)
            return null;

        var (kind, isXref) = Parse(offset, _input.Length, static parser =>
        {
            var token = parser.Lexer.Peek();
            return (token.Kind, parser.Lexer.IsKeyword(token, "xref"u8));
        });
        if (isXref)
            return Parse(offset, _input.Length, ReadTable);

        if (kind == PdfTokenKind.Integer)
            return ReadStreamSection(offset);

        return null;
    }

    private XrefSection? ReadTable(PdfObjectParser parser)
    {
        var lexer = parser.Lexer;
        lexer.Next();
        var entries = new Dictionary<int, XrefEntry>();
        var maxEntries = Math.Min(_input.Length / 10, int.MaxValue);
        while (true)
        {
            var token = lexer.Next();
            if (lexer.IsKeyword(token, "trailer"u8))
                break;

            if (token.Kind != PdfTokenKind.Integer)
                return null;

            var countToken = lexer.Next();
            if (countToken.Kind != PdfTokenKind.Integer || token.IntegerValue is < 0 or > int.MaxValue || countToken.IntegerValue < 0 || countToken.IntegerValue > maxEntries)
                return null;

            var start = (int)token.IntegerValue;
            var count = (int)countToken.IntegerValue;
            for (var i = 0; i < count; i++)
            {
                var offsetToken = lexer.Next();
                var generationToken = lexer.Next();
                var typeToken = lexer.Next();
                if (offsetToken.Kind != PdfTokenKind.Integer || generationToken.Kind != PdfTokenKind.Integer || typeToken.Kind != PdfTokenKind.Keyword)
                    return null;

                // A common mistake: a table that starts at 1 with the entry of object 0 ("0000000000 65535 f")
                if (i == 0 && start == 1 && lexer.IsKeyword(typeToken, "f"u8) && offsetToken.IntegerValue == 0 && generationToken.IntegerValue == 65535)
                {
                    start = 0;
                }

                var number = start + i;
                if (number < 0)
                    return null;

                var isUsed = lexer.IsKeyword(typeToken, "n"u8);
                if (!isUsed && !lexer.IsKeyword(typeToken, "f"u8))
                    return null;

                var entry = isUsed && offsetToken.IntegerValue > 0 ? XrefEntry.InFile(offsetToken.IntegerValue, (int)Math.Clamp(generationToken.IntegerValue, 0, ushort.MaxValue)) : XrefEntry.Free;
                entries.TryAdd(number, entry);
            }
        }

        if (parser.ParseObject() is not PdfDictionary trailer)
            return null;

        // A hybrid file (7.5.8.4): the objects of the cross-reference stream are free or absent in the table
        if (trailer.GetResolved(PdfNames.XRefStm) is PdfInteger xrefStm && xrefStm.Value >= 0 && xrefStm.Value < _input.Length)
        {
            var streamSection = ReadStreamSection(xrefStm.Value);
            if (streamSection is not null)
            {
                foreach (var entry in streamSection.Entries)
                {
                    if (!entries.TryGetValue(entry.Key, out var existing) || existing.Type == XrefEntryType.Free)
                    {
                        entries[entry.Key] = entry.Value;
                    }
                }
            }
        }

        return new XrefSection(entries, trailer, isStream: false);
    }

    private XrefSection? ReadStreamSection(long offset)
    {
        if (Parse(offset, _input.Length, static parser => parser.ParseIndirectObject(out _, out _)) is not PdfStream stream)
            return null;

        var dictionary = stream.Dictionary;
        if (!dictionary.IsType(PdfNames.XRef) && !dictionary.ContainsKey(PdfNames.W))
            return null;

        if (dictionary.GetResolved(PdfNames.W) is not PdfArray widthArray || widthArray.Count < 3)
            throw new InvalidPdfContentException("The /W entry of a cross-reference stream is missing or invalid");

        var widths = new int[3];
        for (var i = 0; i < 3; i++)
        {
            widths[i] = (int)widthArray.GetNumber(i, -1);
            if (widths[i] is < 0 or > 8)
                throw new InvalidPdfContentException("The /W entry of a cross-reference stream has an invalid width");
        }

        var entryLength = widths[0] + widths[1] + widths[2];
        if (entryLength == 0)
            throw new InvalidPdfContentException("The /W entry of a cross-reference stream has no width");

        var data = stream.GetDecodedData();
        var size = dictionary.GetInteger(PdfNames.Size, 0);
        var ranges = new List<(long Start, long Count)>();
        if (dictionary.GetResolved(PdfNames.Index) is PdfArray index && index.Count >= 2)
        {
            for (var i = 0; i + 1 < index.Count; i += 2)
            {
                ranges.Add(((long)index.GetNumber(i), (long)index.GetNumber(i + 1)));
            }
        }
        else
        {
            ranges.Add((0, size));
        }

        var entries = new Dictionary<int, XrefEntry>();
        var position = 0;
        foreach (var (start, count) in ranges)
        {
            if (start < 0 || count < 0)
                throw new InvalidPdfContentException("The /Index entry of a cross-reference stream is invalid");

            for (long i = 0; i < count && position + entryLength <= data.Length; i++)
            {
                var number = start + i;
                var type = widths[0] == 0 ? 1 : ReadField(data, position, widths[0]);
                var field2 = ReadField(data, position + widths[0], widths[1]);
                var field3 = ReadField(data, position + widths[0] + widths[1], widths[2]);
                position += entryLength;
                if (number > int.MaxValue)
                    break;

                var entry = type switch
                {
                    0 => XrefEntry.Free,
                    1 when field2 > 0 => XrefEntry.InFile(field2, (int)Math.Clamp(field3, 0, ushort.MaxValue)),
                    2 when field2 is > 0 and <= int.MaxValue && field3 <= int.MaxValue => XrefEntry.InObjectStream((int)field2, (int)field3),
                    _ => XrefEntry.Free,
                };
                entries.TryAdd((int)number, entry);
            }
        }

        return new XrefSection(entries, dictionary, isStream: true);
    }

    private static long ReadField(byte[] data, int position, int width)
    {
        long value = 0;
        for (var i = 0; i < width; i++)
        {
            value = (value << 8) | data[position + i];
        }

        return value < 0 ? long.MaxValue : value;
    }

    private void RegisterEntries(Dictionary<int, XrefEntry> entries)
    {
        foreach (var (number, entry) in entries)
        {
            if (number == 0 || entry.Type == XrefEntryType.Free)
                continue;

            var registered = Objects.Register(number, entry.Generation);
            registered.Offset = entry.Offset;
            registered.ContainerNumber = entry.ContainerNumber;
            registered.IndexInContainer = entry.Index;
        }
    }

    /// <summary>Rebuilds the cross-reference information by scanning the file for objects and trailers.</summary>
    /// <param name="previousTrailer">The trailer of the cross-reference sections, when they were read but have no catalog: its <c>/Encrypt</c> and <c>/ID</c> are the keys of the objects.</param>
    /// <remarks>
    /// The objects are not loaded, except the object streams (to register their objects), the trailers and the catalogs:
    /// the type of an object is found in its bytes first. The encryption dictionary is found before any of them is
    /// loaded, so that every object is decrypted once, when it is loaded.
    /// </remarks>
    private void Repair(PdfDictionary? previousTrailer)
    {
        IsRepaired = true;
        LastXrefOffset = -1;
        var scanner = GetScanner();
        foreach (var (number, location) in scanner.Offsets)
        {
            if (number == 0)
                continue;

            var entry = Objects.GetEntry(number);
            if (entry is not null && entry.State == IndirectObjectState.Loaded && entry.Offset == location.Offset)
                continue;

            var registered = Objects.Register(number, location.Generation);
            registered.Offset = location.Offset;
        }

        // The objects in the order of the file, and what each one may be
        var objects = scanner.Offsets.Where(o => o.Key > 0).OrderBy(o => o.Value.Offset).Select(o => (Number: o.Key, o.Value.Offset)).ToArray();
        var kinds = new CandidateKind[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            kinds[i] = GetCandidateKind(objects[i].Offset, scanner.GetBoundary(objects[i].Offset));
        }

        var (candidates, keywordCandidates) = FindTrailerCandidates(objects, kinds);
        if (Security is null)
        {
            var encrypted = previousTrailer?[PdfNames.Encrypt] is { } and not PdfNull ? previousTrailer : candidates.LastOrDefault(c => c[PdfNames.Encrypt] is { } and not PdfNull);
            if (encrypted is not null)
            {
                Trailer = StripXrefKeys(encrypted);
                InitializeSecurity();

                // The objects loaded before the keys were known are not decrypted: they are loaded again
                foreach (var entry in Objects.Entries)
                {
                    if (entry.Reference.ObjectNumber != _encryptObjectNumber)
                    {
                        PdfIndirectObjectCollection.Unload(entry);
                    }
                }

                _objectStreams.Clear();
            }
        }

        // The objects of the object streams that are not also stored uncompressed
        for (var i = 0; i < objects.Length; i++)
        {
            var number = objects[i].Number;
            if ((kinds[i] & CandidateKind.ObjectStream) == CandidateKind.None || Objects.GetEntry(number) is not { } entry || Objects.Load(entry) is not PdfStream stream || !stream.Dictionary.IsType(PdfNames.ObjStm))
                continue;

            var objectStream = TryGetObjectStream(number);
            if (objectStream is null)
                continue;

            for (var j = 0; j < objectStream.Objects.Length; j++)
            {
                var contained = objectStream.Objects[j].Number;
                if (contained > 0 && !scanner.Offsets.ContainsKey(contained) && Objects.GetEntry(contained) is null)
                {
                    var registered = Objects.Register(contained, 0);
                    registered.ContainerNumber = number;
                    registered.IndexInContainer = j;
                }
            }
        }

        var keys = Trailer;
        Trailer = StripXrefKeys(FindTrailer(candidates, keywordCandidates, objects, kinds));
        if (Security is not null)
        {
            // The document is written with the keys the objects were decrypted with, which the trailer found may not have
            Trailer[PdfNames.Encrypt] = keys[PdfNames.Encrypt];
            Trailer[PdfNames.ID] = keys[PdfNames.ID];
        }
    }

    /// <summary>Gets the trailers of the file: the dictionaries after the <c>trailer</c> keywords, then the dictionaries of the cross-reference streams, each in the order of the file.</summary>
    private (List<PdfDictionary> Candidates, int KeywordCandidates) FindTrailerCandidates((int Number, long Offset)[] objects, CandidateKind[] kinds)
    {
        var scanner = _scanner!;
        var candidates = new List<PdfDictionary>();
        foreach (var offset in scanner.TrailerOffsets)
        {
            try
            {
                if (Parse(offset, scanner.GetBoundary(offset), static parser => parser.ParseObject()) is PdfDictionary trailer)
                {
                    candidates.Add(trailer);
                }
            }
            catch (InvalidPdfContentException)
            {
            }
        }

        var keywordCandidates = candidates.Count;
        for (var i = 0; i < objects.Length; i++)
        {
            if ((kinds[i] & CandidateKind.CrossReferenceStream) == CandidateKind.None)
                continue;

            // The dictionary of a cross-reference stream is read as it is in the file: it is never encrypted (7.6.2)
            var offset = objects[i].Offset;
            try
            {
                if (Parse(offset, scanner.GetBoundary(offset), static parser => parser.ParseIndirectObject(out _, out _)) is PdfStream stream && stream.Dictionary.IsType(PdfNames.XRef))
                {
                    candidates.Add(stream.Dictionary);
                }
            }
            catch (InvalidPdfContentException)
            {
            }
        }

        return (candidates, keywordCandidates);
    }

    private PdfDictionary FindTrailer(List<PdfDictionary> candidates, int keywordCandidates, (int Number, long Offset)[] objects, CandidateKind[] kinds)
    {
        // The last trailer with a catalog, the trailer keywords before the cross-reference streams
        for (var i = keywordCandidates - 1; i >= 0; i--)
        {
            if (candidates[i].GetResolved(PdfNames.Root) is PdfDictionary)
                return candidates[i];
        }

        for (var i = candidates.Count - 1; i >= keywordCandidates; i--)
        {
            if (candidates[i].GetResolved(PdfNames.Root) is PdfDictionary)
                return candidates[i];
        }

        // No trailer: the last catalog of the file is the root
        var trailer = new PdfDictionary();
        for (var i = objects.Length - 1; i >= 0; i--)
        {
            if ((kinds[i] & CandidateKind.Catalog) != CandidateKind.None && Objects.GetEntry(objects[i].Number) is { } entry && Objects.Load(entry) is PdfDictionary dictionary && dictionary.IsType(PdfNames.Catalog))
            {
                trailer[PdfNames.Root] = entry.Reference;
                return trailer;
            }
        }

        // The catalogs in the object streams, which the scan does not see
        foreach (var entry in Objects.Entries.Where(e => e.ContainerNumber > 0).OrderBy(e => e.Reference.ObjectNumber).ToArray())
        {
            if (Objects.Load(entry) is PdfDictionary dictionary && dictionary.IsType(PdfNames.Catalog))
            {
                trailer[PdfNames.Root] = entry.Reference;
            }
        }

        return trailer;
    }

    /// <summary>
    /// Finds from the bytes of an object what it may be: the names of the types its dictionary contains. The object is not
    /// parsed; an object whose dictionary is too long or has escaped names may be anything.
    /// </summary>
    private CandidateKind GetCandidateKind(long offset, long boundary)
    {
        var length = (int)Math.Min(boundary - offset, MaxCandidateLength);
        if (length <= 0)
            return CandidateKind.None;

        byte[]? rented = null;
        try
        {
            ReadOnlySpan<byte> span;
            if (_input.IsInMemory)
            {
                span = _input.GetMemory(offset, length).Span;
            }
            else
            {
                rented = ArrayPool<byte>.Shared.Rent(length);
                span = rented.AsSpan(0, _input.Read(offset, rented.AsSpan(0, length)));
            }

            // The dictionary ends before the data of a stream
            var stream = span.IndexOf("stream"u8);
            var dictionary = stream >= 0 ? span[..stream] : span;
            if (dictionary.Length == MaxCandidateLength || dictionary.Contains((byte)'#'))
                return CandidateKind.All;

            var kind = CandidateKind.None;
            if (stream >= 0 && dictionary.IndexOf("/ObjStm"u8) >= 0)
            {
                kind |= CandidateKind.ObjectStream;
            }

            if (stream >= 0 && dictionary.IndexOf("/XRef"u8) >= 0)
            {
                kind |= CandidateKind.CrossReferenceStream;
            }

            if (dictionary.IndexOf("/Catalog"u8) >= 0)
            {
                kind |= CandidateKind.Catalog;
            }

            return kind;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Gets the entries of the trailer that are not about its cross-reference section (<c>/Prev</c>, <c>/XRefStm</c>,
    /// and the entries of a cross-reference stream, 7.5.8.2): the writer writes them again, and keeps the others,
    /// including the private ones (7.5.6: an update contains the entries of the previous trailer).
    /// </summary>
    private static PdfDictionary StripXrefKeys(PdfDictionary trailer)
    {
        var result = new PdfDictionary();
        foreach (var (key, value) in trailer)
        {
            // The entries are set as parsed: an incremental save compares the trailer with what was read
            if (value is not PdfNull && !PdfFileWriter.IsCrossReferenceKey(key))
            {
                result.SetParsed(key, value);
            }
        }

        return result;
    }

    private PdfObject? TryLoadAt(long offset, int number, int? generation)
    {
        if (offset < 0 || offset >= _input.Length)
            return null;

        // An object the scan found ends before the next object or trailer at the latest
        var limit = _scanner is not null && _scanner.Offsets.TryGetValue(number, out var found) && found.Offset == offset ? _scanner.GetBoundary(offset) : _input.Length;
        try
        {
            var (value, foundNumber, foundGeneration) = Parse(offset, limit, static parser =>
            {
                var value = parser.ParseIndirectObject(out var foundNumber, out var foundGeneration);
                return (value, foundNumber, foundGeneration);
            });
            if (value is null || foundNumber != number || (generation is { } expected && foundGeneration != expected))
                return null;

            if (Security is not null && number != _encryptObjectNumber)
            {
                value = Security.Decrypt(value, number, foundGeneration);
            }

            return value;
        }
        catch (InvalidPdfContentException)
        {
            return null;
        }
    }

    private PdfObject? TryLoadCompressed(int containerNumber, int index, int number)
    {
        var objectStream = TryGetObjectStream(containerNumber);
        if (objectStream is null)
            return null;

        var objects = objectStream.Objects;
        if ((uint)index >= (uint)objects.Length || objects[index].Number != number)
        {
            index = objectStream.IndexOf(number);
            if (index < 0)
                return null;
        }

        var start = (long)objectStream.First + objects[index].Offset;
        if (start < 0 || start >= objectStream.Data.Length)
            return null;

        var lexer = new PdfLexer(objectStream.Data, (int)start);
        var parser = new PdfObjectParser(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits, NameCache = _names };
        try
        {
            return parser.ParseObject();
        }
        catch (InvalidPdfContentException)
        {
            return null;
        }
    }

    private ObjectStreamData? TryGetObjectStream(int number)
    {
        if (_objectStreams.TryGetValue(number, out var cached))
            return cached;

        var entry = Objects.GetEntry(number);
        if (entry is null || entry.ContainerNumber > 0 || Objects.Load(entry) is not PdfStream stream)
            return null;

        byte[] data;
        try
        {
            data = stream.GetDecodedData();
        }
        catch (InvalidPdfContentException)
        {
            return null;
        }
        catch (UnsupportedPdfFeatureException)
        {
            return null;
        }

        var count = stream.Dictionary.GetInteger(PdfNames.N, 0);
        var first = stream.Dictionary.GetInteger(PdfNames.First, 0);
        if (count < 0 || first < 0 || first > data.Length || count > data.Length / 2)
            return null;

        var lexer = new PdfLexer(data);
        var objects = new List<(int Number, int Offset)>((int)count);
        for (var i = 0; i < count; i++)
        {
            var numberToken = lexer.Next();
            var offsetToken = lexer.Next();
            if (numberToken.Kind != PdfTokenKind.Integer || offsetToken.Kind != PdfTokenKind.Integer || numberToken.IntegerValue is < 0 or > int.MaxValue || offsetToken.IntegerValue is < 0 or > int.MaxValue)
                break;

            objects.Add(((int)numberToken.IntegerValue, (int)offsetToken.IntegerValue));
        }

        var result = new ObjectStreamData(data, (int)first, [.. objects]);
        _objectStreams.Add(number, result);
        return result;
    }

    /// <summary>
    /// Parses at an offset of the file. In memory, the lexer reads the file up to <paramref name="limit"/>; from a stream,
    /// it reads a window that grows until the object fits in it (the lexer did not reach the end of the window) or the
    /// window reaches <paramref name="limit"/>.
    /// </summary>
    private T Parse<T>(long offset, long limit, ParseFunc<T> parse)
    {
        if (_input is MemoryPdfInput memory)
        {
            var lexer = new PdfLexer(memory.Data[..(int)limit], (int)offset);
            return parse(CreateParser(lexer, new PdfInputWindow(_input, 0, limit)));
        }

        var size = (int)Math.Min(limit - offset, InitialWindowSize);
        while (true)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
            try
            {
                var read = _input.Read(offset, buffer.AsSpan(0, size));
                var canGrow = offset + read < limit && size < Array.MaxLength;
                var lexer = new PdfLexer(buffer.AsMemory(0, read));
                var parser = CreateParser(lexer, new PdfInputWindow(_input, offset, limit));
                try
                {
                    var result = parse(parser);
                    if (!canGrow || !lexer.ReachedEnd)
                        return result;
                }
                catch (InvalidPdfContentException) when (canGrow && lexer.ReachedEnd)
                {
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            size = (int)Math.Min(Math.Min(limit - offset, (long)size * 4), Array.MaxLength);
        }
    }

    private PdfObjectParser CreateParser(PdfLexer lexer, PdfInputWindow window) => new(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits, Window = window, NameCache = _names };

    private static long IndexOf(PdfInput input, ReadOnlySpan<byte> value)
    {
        if (input is MemoryPdfInput memory)
            return memory.Data.Span.IndexOf(value);

        const int ChunkSize = 1024 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize + value.Length);
        try
        {
            for (long offset = 0; offset < input.Length; offset += ChunkSize)
            {
                var read = input.Read(offset, buffer.AsSpan(0, ChunkSize + value.Length - 1));
                var index = buffer.AsSpan(0, read).IndexOf(value);
                if (index >= 0)
                    return offset + index;
            }

            return -1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Finds the last occurrence of a keyword delimited by non-regular characters, reading the input backwards.</summary>
    private static long LastIndexOfKeyword(PdfInput input, ReadOnlySpan<byte> keyword)
    {
        if (input is MemoryPdfInput memory)
            return LastIndexOfKeyword(memory.Data.Span, keyword, memory.Data.Length, minIndex: 0);

        var buffer = ArrayPool<byte>.Shared.Rent(StartXrefSearchChunk + keyword.Length + 2);
        try
        {
            // Each chunk [start, end) is read with the byte before it and the bytes of a keyword that starts at its end
            for (var end = input.Length; end > 0; end -= StartXrefSearchChunk)
            {
                var start = Math.Max(0, end - StartXrefSearchChunk);
                var readStart = Math.Max(0, start - 1);
                var readEnd = Math.Min(input.Length, end + keyword.Length + 1);
                var read = input.Read(readStart, buffer.AsSpan(0, (int)(readEnd - readStart)));
                var span = buffer.AsSpan(0, read);
                var searchEnd = (int)Math.Min(span.Length, end - readStart + keyword.Length - 1);
                var index = LastIndexOfKeyword(span, keyword, searchEnd, minIndex: (int)(start - readStart));
                if (index >= 0)
                    return readStart + index;
            }

            return -1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int LastIndexOfKeyword(ReadOnlySpan<byte> span, ReadOnlySpan<byte> keyword, int end, int minIndex)
    {
        while (end > 0)
        {
            var index = span[..end].LastIndexOf(keyword);
            if (index < minIndex)
                return -1;

            var after = index + keyword.Length;
            if ((index == 0 || !PdfLexer.IsRegular(span[index - 1])) && (after >= span.Length || !PdfLexer.IsRegular(span[after])))
                return index;

            end = index;
        }

        return -1;
    }
}
