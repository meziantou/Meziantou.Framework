using System.Text;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Security;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Reads the structure of a file (ISO 32000-2, 7.5): the header, the chain of cross-reference sections (tables, streams,
/// hybrid files, incremental updates), and the indirect objects when they are first resolved. When the cross-reference
/// information is missing or wrong, the objects are found by scanning the file (<see cref="PdfRecoveryScanner"/>).
/// </summary>
internal sealed class PdfFileReader : IPdfObjectLoader
{
    private const int MaxXrefSections = 4096;
    private const int MaxCachedObjectStreams = 64;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly PdfReadOptions _options;
    private readonly Dictionary<int, ObjectStreamData> _objectStreams = [];
    private PdfRecoveryScanner? _scanner;
    private int _encryptObjectNumber = -1;

    private PdfFileReader(ReadOnlyMemory<byte> data, PdfReadOptions options, PdfIndirectObjectCollection objects)
    {
        _data = data;
        _options = options;
        Objects = objects;
        DecodeLimits = new PdfDecodeLimits(options.MaxDecodedStreamLength);
    }

    public PdfIndirectObjectCollection Objects { get; }

    public PdfDecodeLimits DecodeLimits { get; }

    public PdfVersion HeaderVersion { get; private set; } = PdfVersion.Pdf14;

    /// <summary>Gets the offset of the header: the offsets of some files count from it, not from the start of the file.</summary>
    public int HeaderOffset { get; private set; }

    public PdfDictionary Trailer { get; private set; } = new();

    /// <summary>Gets a value indicating whether the cross-reference information was rebuilt by scanning the file.</summary>
    public bool IsRepaired { get; private set; }

    /// <summary>Gets the offset of the last cross-reference section, the <c>/Prev</c> of an incremental update.</summary>
    public long LastXrefOffset { get; private set; } = -1;

    /// <summary>Gets a value indicating whether the last cross-reference section is a stream.</summary>
    public bool LastXrefIsStream { get; private set; }

    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Gets the options the file was read with, among them the password of an encrypted file.</summary>
    public PdfReadOptions Options => _options;

    /// <summary>Gets the security handler of an encrypted file, which decrypts the objects when they are loaded.</summary>
    public StandardSecurityHandler? Security { get; private set; }

    public static PdfFileReader Open(ReadOnlyMemory<byte> data, PdfReadOptions options)
    {
        var objects = new PdfIndirectObjectCollection();
        var reader = new PdfFileReader(data, options, objects);
        objects.Loader = reader;
        reader.ReadHeader();
        if (!reader.TryReadCrossReferences())
        {
            reader.Repair();
        }

        // The objects are decrypted from the first one that is loaded after the encryption dictionary
        reader.InitializeSecurity();
        if (reader.Trailer.GetResolved(PdfNames.Root) is not PdfDictionary && !reader.IsRepaired)
        {
            var trailer = reader.Trailer;
            reader.Repair();
            if (reader.Security is not null)
            {
                // The objects are decrypted with the /Encrypt and the /ID of the trailer of the cross-reference sections,
                // which the trailer the repair finds may not have: the document is written with the same keys
                reader.Trailer[PdfNames.Encrypt] = trailer[PdfNames.Encrypt];
                reader.Trailer[PdfNames.ID] = trailer[PdfNames.ID];
            }

            reader.InitializeSecurity();
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
        _scanner ??= PdfRecoveryScanner.Scan(_data);
        if (_scanner.Offsets.TryGetValue(number, out var found) && found.Offset != entry.Offset)
        {
            var value = TryLoadAt(found.Offset, number, generation: null);
            if (value is not null)
                return value;
        }

        return PdfNull.Instance;
    }

    /// <summary>Authenticates the password of an encrypted file (7.6): the encryption dictionary is not encrypted itself.</summary>
    private void InitializeSecurity()
    {
        if (Security is not null || Trailer[PdfNames.Encrypt] is not { } encrypt)
            return;

        if (encrypt is PdfReference reference)
        {
            _encryptObjectNumber = reference.ObjectNumber;
        }

        if (PdfReference.ResolveIfReference(encrypt) is not PdfDictionary dictionary)
            throw new InvalidPdfContentException("The /Encrypt entry of the trailer is not a dictionary");

        var id = Trailer.GetResolved(PdfNames.ID) is PdfArray ids && ids.TryGetResolved<PdfString>(0, out var first) ? first.Span : [];
        Security = StandardSecurityHandler.Open(dictionary, id, _options.Password);
    }

    private void ReadHeader()
    {
        var span = _data.Span;
        var searchLength = Math.Min(span.Length, 1024);
        var index = span[..searchLength].IndexOf("%PDF-"u8);
        if (index < 0)
        {
            // Some producers prepend garbage beyond 1 KiB, and some files have no header at all: like other readers, the
            // file is read anyway, and it is rejected when no catalog can be found
            index = span.IndexOf("%PDF-"u8);
            if (index < 0)
                return;
        }

        HeaderOffset = index;
        if (index + 8 <= span.Length && PdfVersion.TryParse(Encoding.ASCII.GetString(span.Slice(index + 5, 3)), out var version))
        {
            HeaderVersion = version;
        }
    }

    private bool TryReadCrossReferences()
    {
        var span = _data.Span;
        var startxref = LastIndexOfKeyword(span, "startxref"u8);
        if (startxref < 0)
            return false;

        var lexer = new PdfLexer(_data, startxref + "startxref".Length);
        var offsetToken = lexer.Next();
        if (offsetToken.Kind != PdfTokenKind.Integer || offsetToken.IntegerValue < 0 || offsetToken.IntegerValue >= span.Length)
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
                    // The newest trailer wins; an update that omits an entry inherits it
                    foreach (var key in new[] { PdfNames.Root, PdfNames.Info, PdfNames.ID, PdfNames.Encrypt })
                    {
                        if (!trailer.ContainsKey(key) && section.Trailer[key] is { } value)
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
        catch (InvalidPdfContentException)
        {
            if (trailer is null)
                return false;
        }

        if (trailer is null)
            return false;

        Trailer = StripXrefKeys(trailer);
        RegisterEntries(entries);
        Objects.Reserve((int)Math.Clamp(Trailer.GetInteger(PdfNames.Size, 0) - 1, 0, int.MaxValue - 1));
        return true;
    }

    private XrefSection? ReadSection(long offset)
    {
        var section = TryReadSection(offset);
        if (section is null && HeaderOffset > 0 && offset + HeaderOffset < _data.Length)
        {
            section = TryReadSection(offset + HeaderOffset);
        }

        return section;
    }

    private XrefSection? TryReadSection(long offset)
    {
        if (offset < 0 || offset >= _data.Length)
            return null;

        var lexer = new PdfLexer(_data, (int)offset);
        var token = lexer.Peek();
        if (lexer.IsKeyword(token, "xref"u8))
        {
            lexer.Next();
            return ReadTable(lexer);
        }

        if (token.Kind == PdfTokenKind.Integer)
            return ReadStreamSection(offset);

        return null;
    }

    private XrefSection? ReadTable(PdfLexer lexer)
    {
        var entries = new Dictionary<int, XrefEntry>();
        var maxEntries = _data.Length / 10;
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

        var parser = new PdfObjectParser(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits };
        if (parser.ParseObject() is not PdfDictionary trailer)
            return null;

        // A hybrid file (7.5.8.4): the objects of the cross-reference stream are free or absent in the table
        if (trailer.GetResolved(PdfNames.XRefStm) is PdfInteger xrefStm && xrefStm.Value >= 0 && xrefStm.Value < _data.Length)
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
        var lexer = new PdfLexer(_data, (int)offset);
        var parser = new PdfObjectParser(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits };
        if (parser.ParseIndirectObject(out _, out _) is not PdfStream stream)
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
    private void Repair()
    {
        IsRepaired = true;
        LastXrefOffset = -1;
        _scanner ??= PdfRecoveryScanner.Scan(_data);
        foreach (var (number, location) in _scanner.Offsets)
        {
            if (number == 0)
                continue;

            var entry = Objects.GetEntry(number);
            if (entry is not null && entry.State == IndirectObjectState.Loaded && entry.Offset == location.Offset)
                continue;

            var registered = Objects.Register(number, location.Generation);
            registered.Offset = location.Offset;
        }

        // The objects of the object streams that are not also stored uncompressed
        foreach (var (number, _) in _scanner.Offsets.ToArray())
        {
            if (Objects.GetEntry(number) is { } entry && Load(entry, Objects) is PdfStream stream && stream.Dictionary.IsType(PdfNames.ObjStm))
            {
                var objectStream = TryGetObjectStream(number);
                if (objectStream is null)
                    continue;

                for (var i = 0; i < objectStream.Objects.Length; i++)
                {
                    var contained = objectStream.Objects[i].Number;
                    if (contained > 0 && !_scanner.Offsets.ContainsKey(contained) && Objects.GetEntry(contained) is null)
                    {
                        var registered = Objects.Register(contained, 0);
                        registered.ContainerNumber = number;
                        registered.IndexInContainer = i;
                    }
                }
            }
        }

        Trailer = StripXrefKeys(FindTrailer());
    }

    private static PdfObject Load(IndirectObjectEntry entry, PdfIndirectObjectCollection objects) => objects.Load(entry);

    private PdfDictionary FindTrailer()
    {
        PdfDictionary? best = null;
        foreach (var offset in _scanner!.TrailerOffsets)
        {
            var lexer = new PdfLexer(_data, offset);
            var parser = new PdfObjectParser(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits };
            try
            {
                if (parser.ParseObject() is PdfDictionary trailer && trailer.GetResolved(PdfNames.Root) is PdfDictionary)
                {
                    best = trailer;
                }
            }
            catch (InvalidPdfContentException)
            {
            }
        }

        if (best is not null)
            return best;

        // The dictionaries of the cross-reference streams are trailers too
        foreach (var entry in Objects.Entries.OrderBy(e => e.Offset).ToArray())
        {
            if (Objects.Load(entry) is PdfStream stream && stream.Dictionary.IsType(PdfNames.XRef) && stream.Dictionary.GetResolved(PdfNames.Root) is PdfDictionary)
            {
                best = stream.Dictionary;
            }
        }

        if (best is not null)
            return best;

        // No trailer: the last catalog of the file is the root
        var trailerDictionary = new PdfDictionary();
        foreach (var entry in Objects.Entries.OrderBy(e => e.Offset).ToArray())
        {
            if (Objects.Load(entry) is PdfDictionary dictionary && dictionary.IsType(PdfNames.Catalog))
            {
                trailerDictionary[PdfNames.Root] = entry.Reference;
            }
        }

        return trailerDictionary;
    }

    private static PdfDictionary StripXrefKeys(PdfDictionary trailer)
    {
        var result = new PdfDictionary();
        foreach (var key in new[] { PdfNames.Size, PdfNames.Root, PdfNames.Info, PdfNames.ID, PdfNames.Encrypt })
        {
            if (trailer[key] is { } value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private PdfObject? TryLoadAt(long offset, int number, int? generation)
    {
        if (offset < 0 || offset >= _data.Length)
            return null;

        var lexer = new PdfLexer(_data, (int)offset);
        var parser = new PdfObjectParser(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits };
        try
        {
            var value = parser.ParseIndirectObject(out var foundNumber, out var foundGeneration);
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
            index = Array.FindIndex(objects, o => o.Number == number);
            if (index < 0)
                return null;
        }

        var start = (long)objectStream.First + objects[index].Offset;
        if (start < 0 || start >= objectStream.Data.Length)
            return null;

        var lexer = new PdfLexer(objectStream.Data, (int)start);
        var parser = new PdfObjectParser(lexer, Objects, _options.MaxNestingDepth) { DecodeLimits = DecodeLimits };
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
        if (_objectStreams.Count >= MaxCachedObjectStreams)
        {
            _objectStreams.Clear();
        }

        _objectStreams[number] = result;
        return result;
    }

    private static int LastIndexOfKeyword(ReadOnlySpan<byte> span, ReadOnlySpan<byte> keyword)
    {
        var end = span.Length;
        while (end > 0)
        {
            var index = span[..end].LastIndexOf(keyword);
            if (index < 0)
                return -1;

            var after = index + keyword.Length;
            if ((index == 0 || !PdfLexer.IsRegular(span[index - 1])) && (after >= span.Length || !PdfLexer.IsRegular(span[after])))
                return index;

            end = index;
        }

        return -1;
    }
}
