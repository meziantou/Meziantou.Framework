using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Security;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// Writes a document (ISO 32000-2, 7.5): a full save writes the objects reachable from the trailer, numbered in the
/// order they are reached; an incremental save (7.5.6) appends the objects that changed to the original file.
/// </summary>
internal sealed partial class PdfFileWriter : IDisposable
{
    private const int ObjectsPerObjectStream = 100;

    // A comment of bytes above 127 after the header tells transfer programs the file is binary (7.5.2)
    private static readonly byte[] BinaryMarker = [(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'];

    private static readonly PdfName ByteRangeName = PdfName.Create("ByteRange");

    private readonly PdfDocument _document;
    private readonly PdfSaveOptions _options;
    private PdfByteWriter _output;
    private readonly CancellationToken _cancellationToken;

    // The buffers reused by every stream of the save: the compressed data, and the parts of an object stream
    private readonly MemoryStream _compressed = new();
    private PdfByteWriter? _objectStreamHeader;
    private PdfByteWriter? _objectStreamBody;

    // The security handler that encrypts the strings and streams of the objects, and the first part of the file
    // identifier its keys depend on (7.6.3.3)
    private StandardSecurityHandler? _security;
    private byte[]? _firstId;

    private PdfFileWriter(PdfDocument document, PdfSaveOptions options, PdfByteWriter output, CancellationToken cancellationToken)
    {
        _document = document;
        _options = options;
        _output = output;
        _cancellationToken = cancellationToken;
    }

    public static void WriteFull(PdfDocument document, PdfSaveOptions options, PdfByteWriter output, CancellationToken cancellationToken = default)
    {
        using var writer = new PdfFileWriter(document, options, output, cancellationToken);
        writer.WriteFull();
        output.Flush();
    }

    public static void WriteIncremental(PdfDocument document, PdfSaveOptions options, PdfByteWriter output, CancellationToken cancellationToken = default)
    {
        using var writer = new PdfFileWriter(document, options, output, cancellationToken);
        writer.WriteIncremental();
        output.Flush();
    }

    public void Dispose() => _compressed.Dispose();

    private void WriteFull()
    {
        if (_options.Linearize)
        {
            WriteLinearized();
            return;
        }

        var objects = _document.Objects;
        var trailer = _document.Trailer;

        // Number the objects in the order they are reached from the trailer
        var ordered = new List<IndirectObjectEntry>();
        var numbers = new Dictionary<IndirectObjectEntry, int>();
        var queue = new Queue<IndirectObjectEntry>();
        void Visit(PdfObject value)
        {
            var stack = new Stack<PdfObject>();
            stack.Push(value);
            while (stack.Count > 0)
            {
                switch (stack.Pop())
                {
                    case PdfReference reference:
                        if (ReferenceEquals(reference.Owner, objects) && objects.GetEntry(reference) is { } entry && !numbers.ContainsKey(entry) && objects.Load(entry) is not PdfNull)
                        {
                            numbers.Add(entry, numbers.Count + 1);
                            ordered.Add(entry);
                            queue.Enqueue(entry);
                        }

                        break;
                    case PdfArray array:
                        for (var i = array.Count - 1; i >= 0; i--)
                        {
                            stack.Push(array[i]);
                        }

                        break;
                    case PdfDictionary dictionary:
                        PushEntries(stack, dictionary, skipLength: false);
                        break;
                    case PdfStream stream:
                        PushEntries(stack, stream.Dictionary, skipLength: true);
                        break;
                }
            }
        }

        var root = trailer[PdfNames.Root];
        var info = OmitsInformation() ? null : trailer[PdfNames.Info];
        var others = GetOtherTrailerEntries(trailer);
        foreach (var value in new[] { root, info }.Concat(others.Select(e => e.Value)))
        {
            if (value is not null)
            {
                Visit(value);
            }
        }

        while (queue.Count > 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Visit(queue.Dequeue().Value!);
        }

        // The encryption: the keys of the file the document was read from, or new keys; the encryption dictionary is an
        // object of the writer, numbered after the objects of the document
        var writerObjects = new PdfIndirectObjectCollection();
        if (_document.Encryption is { } encryption)
        {
            _firstId = GetOriginalFirstId() ?? RandomNumberGenerator.GetBytes(16);
            _security = encryption.Handler ?? StandardSecurityHandler.Create(encryption, _firstId);
        }

        var references = new RenumberingReferenceWriter(numbers, objects, writerObjects);
        // PDF/A-1 is PDF 1.4 (ISO 19005-1, 6.1.4): a cross-reference table, without object streams
        var isPdfA1 = Conformance.PdfALevel.GetPart(_document.PdfAConformance) == 1;
        if (isPdfA1 && _options.Version is { } requested && requested != PdfVersion.Pdf14)
            throw new InvalidOperationException($"PDF/A-1 is PDF 1.4: the requested version is {requested}");

        var useObjectStreams = _options.UseObjectStreams && !isPdfA1;
        var version = GetVersion(useObjectStreams);
        _output.BeginHash();
        WriteHeader(version);

        var offsets = new long[ordered.Count + 1];
        var compressed = new List<(int Number, PdfObject Value)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var number = i + 1;
            var value = ordered[i].Value!;
            if (useObjectStreams && value is not PdfStream)
            {
                compressed.Add((number, value));
                continue;
            }

            offsets[number] = _output.Position;
            WriteIndirectObject(number, 0, value, references);
        }

        var nextNumber = ordered.Count + 1;
        var containers = new (int Number, int Index)[ordered.Count + 1];
        var objectStreamOffsets = new List<(int Number, long Offset)>();
        foreach (var (containerNumber, batch, offset) in WriteObjectStreams(compressed, nextNumber, references))
        {
            nextNumber++;
            for (var i = 0; i < batch.Count; i++)
            {
                containers[batch[i].Number] = (containerNumber, i);
            }

            objectStreamOffsets.Add((containerNumber, offset));
        }

        // The encryption dictionary is not encrypted, and not in an object stream (7.5.7)
        PdfReference? encryptReference = null;
        long encryptOffset = 0;
        if (_security is not null)
        {
            encryptReference = new PdfReference(writerObjects, nextNumber++, 0);
            encryptOffset = _output.Position;
            WriteIndirectObject(encryptReference.ObjectNumber, 0, _security.Dictionary, references, encrypt: false);
        }

        var id = ComputeId(_output.EndHash(), isUpdate: false);
        if (useObjectStreams)
        {
            var xrefNumber = nextNumber;
            var size = xrefNumber + 1;
            var xrefOffset = _output.Position;
            var entries = new (int Type, long Field2, long Field3)[size];
            entries[0] = (0, 0, 65535);
            for (var number = 1; number <= ordered.Count; number++)
            {
                entries[number] = containers[number].Number > 0 ? (2, containers[number].Number, containers[number].Index) : (1, offsets[number], 0);
            }

            foreach (var (number, offset) in objectStreamOffsets)
            {
                entries[number] = (1, offset, 0);
            }

            entries[xrefNumber] = (1, xrefOffset, 0);
            if (encryptReference is not null)
            {
                entries[encryptReference.ObjectNumber] = (1, encryptOffset, 0);
            }

            var dictionary = CreateTrailer(size, root, info, id, others, encryptReference, prev: null);
            WriteXrefStream(xrefNumber, dictionary, [(0, size)], entries, references);
            WriteStartXref(xrefOffset);
        }
        else
        {
            var size = nextNumber;
            var xrefOffset = _output.Position;
            _output.WriteAscii("xref\n0 ");
            _output.WriteInteger(size);
            _output.WriteNewLine();
            _output.WriteAscii("0000000000 65535 f \n");
            for (var number = 1; number < size; number++)
            {
                WriteTableEntry(number == encryptReference?.ObjectNumber ? encryptOffset : offsets[number], 0);
            }

            var dictionary = CreateTrailer(size, root, info, id, others, encryptReference, prev: null);
            _output.WriteAscii("trailer\n");
            PdfObjectSerializer.Write(_output, dictionary, references);
            _output.WriteNewLine();
            WriteStartXref(xrefOffset);
        }
    }

    private void WriteIncremental()
    {
        var reader = _document.Reader ?? throw new InvalidOperationException("Only a document read from a file can be saved incrementally");
        if (_options.Linearize)
            throw new InvalidOperationException("An incremental update cannot be linearized: save the document in full");

        if (reader.IsRepaired || reader.LastXrefOffset < 0)
            throw new InvalidOperationException("The cross-reference information of the file was rebuilt: the document must be saved in full");

        if (_options.Version is { } requested && requested > reader.HeaderVersion && _document.Catalog.GetName(PdfNames.Version) is var current && (current is null || !PdfVersion.TryParse(current.Value, out var catalogVersion) || catalogVersion < requested))
        {
            _document.Catalog[PdfNames.Version] = PdfName.Create(requested.ToString());
        }

        // The objects of the update are encrypted with the keys of the file, whose encryption cannot change
        _security = reader.Security;
        if (_document.Encryption?.Handler != _security || (_security is null && _document.Encryption is not null))
            throw new InvalidOperationException("The encryption of a document cannot change in an incremental update: the document must be saved in full");

        var objects = _document.Objects;
        var trailer = _document.Trailer;
        var changed = new List<IndirectObjectEntry>();
        var removed = new List<IndirectObjectEntry>();
        foreach (var entry in objects.Entries.OrderBy(e => e.Reference.ObjectNumber))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (entry.State == IndirectObjectState.Free)
            {
                removed.Add(entry);
            }
            else if (entry.State == IndirectObjectState.Loaded && (entry.IsNew || entry.OriginalFingerprint is null || !entry.OriginalFingerprint.Matches(entry.Value!)))
            {
                changed.Add(entry);
            }
        }

        // Nothing changed: the file is written as it is, rather than with an update without objects, whose
        // cross-reference section would have no subsection
        var source = reader.Data;
        if (changed.Count == 0 && removed.Count == 0 && _document.OriginalTrailerFingerprint is { } original && original.Matches(trailer))
        {
            _output.Write(source);
            return;
        }

        // A cross-reference table has at least one subsection (7.5.4): an update of the trailer alone writes the catalog
        // again (a cross-reference stream is an entry of its own section)
        if (changed.Count == 0 && removed.Count == 0 && !reader.LastXrefIsStream && trailer[PdfNames.Root] is PdfReference root && objects.GetEntry(root) is { } rootEntry && objects.Load(rootEntry) is not PdfNull)
        {
            changed.Add(rootEntry);
        }

        _output.Write(source);
        if (source.Length > 0 && source.Span[^1] is not ((byte)'\n' or (byte)'\r'))
        {
            _output.WriteNewLine();
        }

        // The objects of the update are written in object streams when the file has cross-reference streams, except
        // those an object stream cannot contain (7.5.7)
        var useObjectStreams = reader.LastXrefIsStream && _options.UseObjectStreams;
        var encrypt = trailer[PdfNames.Encrypt] as PdfReference;
        var compressed = new List<(int Number, PdfObject Value)>();
        if (useObjectStreams)
        {
            foreach (var entry in changed)
            {
                if (CanBeInObjectStream(entry, encrypt))
                {
                    compressed.Add((entry.Reference.ObjectNumber, entry.Value!));
                }
            }
        }

        var objectStreamCount = (compressed.Count + ObjectsPerObjectStream - 1) / ObjectsPerObjectStream;
        var newNumbers = objectStreamCount + (reader.LastXrefIsStream ? 1 : 0);
        if ((long)objects.MaxObjectNumber + newNumbers + 1 > int.MaxValue)
            throw new InvalidOperationException($"No object number is left for the update: the document uses the object number {objects.MaxObjectNumber}. Save it in full.");

        _output.BeginHash();
        var references = new IncrementalReferenceWriter(objects);
        var entries = new List<(int Number, (int Type, long Field2, long Field3) Entry)>();
        var compressedNumbers = compressed.Select(c => c.Number).ToHashSet();
        foreach (var entry in changed)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (compressedNumbers.Contains(entry.Reference.ObjectNumber))
                continue;

            entries.Add((entry.Reference.ObjectNumber, (1, _output.Position, entry.Reference.Generation)));
            WriteIndirectObject(entry.Reference.ObjectNumber, entry.Reference.Generation, entry.Value!, references);
        }

        var nextNumber = objects.MaxObjectNumber + 1;
        foreach (var (containerNumber, batch, offset) in WriteObjectStreams(compressed, nextNumber, references))
        {
            nextNumber++;
            entries.Add((containerNumber, (1, offset, 0)));
            for (var i = 0; i < batch.Count; i++)
            {
                entries.Add((batch[i].Number, (2, containerNumber, i)));
            }
        }

        // A removed object is a free entry with the next generation, so that a later object with this number does not
        // resolve the references to the removed one (7.5.4); a generation that reached 65535 is not used again
        foreach (var entry in removed)
        {
            entries.Add((entry.Reference.ObjectNumber, (0, 0, Math.Min(entry.Reference.Generation + 1, 65535))));
        }

        var info = OmitsInformation() ? null : trailer[PdfNames.Info];
        var others = GetOtherTrailerEntries(trailer);
        var id = ComputeId(_output.EndHash(), isUpdate: true);
        var prev = PdfInteger.Get(reader.LastXrefOffset);
        if (reader.LastXrefIsStream)
        {
            var xrefNumber = nextNumber;
            var size = xrefNumber + 1;
            var xrefOffset = _output.Position;
            entries.Add((xrefNumber, (1, xrefOffset, 0)));
            entries.Sort((a, b) => a.Number.CompareTo(b.Number));
            var ranges = GetRanges(entries.Select(e => e.Number));
            var dictionary = CreateTrailer(size, trailer[PdfNames.Root], info, id, others, encrypt: trailer[PdfNames.Encrypt], prev);
            WriteXrefStream(xrefNumber, dictionary, ranges, entries.Select(e => e.Entry).ToArray(), references);
            WriteStartXref(xrefOffset);
        }
        else
        {
            var size = nextNumber;
            var xrefOffset = _output.Position;
            entries.Sort((a, b) => a.Number.CompareTo(b.Number));
            _output.WriteAscii("xref\n");
            var index = 0;
            foreach (var (start, count) in GetRanges(entries.Select(e => e.Number)))
            {
                _output.WriteInteger(start);
                _output.WriteSpace();
                _output.WriteInteger(count);
                _output.WriteNewLine();
                for (var i = 0; i < count; i++, index++)
                {
                    var (type, field2, field3) = entries[index].Entry;
                    if (type == 0)
                    {
                        WriteFreeTableEntry((int)field3);
                    }
                    else
                    {
                        WriteTableEntry(field2, (int)field3);
                    }
                }
            }

            var dictionary = CreateTrailer(size, trailer[PdfNames.Root], info, id, others, encrypt: trailer[PdfNames.Encrypt], prev);
            _output.WriteAscii("trailer\n");
            PdfObjectSerializer.Write(_output, dictionary, references);
            _output.WriteNewLine();
            WriteStartXref(xrefOffset);
        }
    }

    /// <summary>
    /// Gets a value indicating whether an object of an update can be in an object stream (7.5.7): not a stream, not an
    /// object of a generation other than zero, not the encryption dictionary, and not a signature dictionary, whose byte
    /// ranges are offsets in the file and whose signature is written in place in the file (12.8.1).
    /// </summary>
    private static bool CanBeInObjectStream(IndirectObjectEntry entry, PdfReference? encrypt)
    {
        if (entry.Reference.Generation != 0 || entry.Value is PdfStream || (encrypt is not null && encrypt.ObjectNumber == entry.Reference.ObjectNumber))
            return false;

        return entry.Value is not PdfDictionary dictionary || !dictionary.ContainsKey(ByteRangeName);
    }

    /// <summary>
    /// Gets a value indicating whether the document information dictionary is left out of the trailer: PDF/A-4 only
    /// allows it with page-piece dictionaries (ISO 19005-4, 6.1.3), the XMP metadata has the information.
    /// </summary>
    private bool OmitsInformation() => Conformance.PdfALevel.GetPart(_document.PdfAConformance) == 4 && !_document.Catalog.ContainsKey(PdfName.Create("PieceInfo"));

    private PdfVersion GetVersion(bool useObjectStreams)
    {
        var version = _options.Version ?? _document.Version;
        if (useObjectStreams && version < PdfVersion.Pdf15)
        {
            if (_options.Version is not null)
                throw new InvalidOperationException($"Object streams need PDF 1.5 or later, the requested version is {version}");

            version = PdfVersion.Pdf15;
        }

        // The revisions of the standard security handler: 3 needs PDF 1.4, 4 needs PDF 1.6, 6 needs PDF 2.0
        var encryptionVersion = _security?.Algorithm switch
        {
            PdfEncryptionAlgorithm.Rc4With128BitKey => PdfVersion.Pdf14,
            PdfEncryptionAlgorithm.AesWith128BitKey => PdfVersion.Pdf16,
            PdfEncryptionAlgorithm.AesWith256BitKey => PdfVersion.Pdf20,
            _ => version,
        };
        if (encryptionVersion > version)
        {
            if (_options.Version is not null)
                throw new InvalidOperationException($"The encryption needs PDF {encryptionVersion} or later, the requested version is {version}");

            version = encryptionVersion;
        }

        return version;
    }

    private void WriteHeader(PdfVersion version)
    {
        _output.WriteAscii("%PDF-");
        _output.WriteAscii(version.ToString());
        _output.WriteNewLine();
        _output.Write(BinaryMarker);
    }

    private void WriteIndirectObject(int number, int generation, PdfObject value, ReferenceWriter references, bool encrypt = true)
    {
        if (value is PdfStream stream)
        {
            WriteStreamObject(number, generation, stream.Dictionary, stream.EncodedData, ReadOnlySpan<byte>.Empty, stream.IsCompressible, references, encrypt);
            return;
        }

        WriteObjectHeader(number, generation);

        // The strings of an object are encrypted with the key of its number and generation (7.6.2)
        var security = encrypt ? _security : null;
        PdfStringTransform? encryptString = security is null ? null : text => security.EncryptString(text, number, generation);
        PdfObjectSerializer.Write(_output, value, references, encryptString);
        _output.WriteAscii("\nendobj\n");
    }

    /// <summary>
    /// Writes a stream object whose data is <paramref name="data"/> followed by <paramref name="suffix"/>: compressed when
    /// allowed, then encrypted, with its length direct. The data of a stream of the document is written without a copy
    /// when the output keeps its blocks; the compressed data is in a buffer reused by the next stream.
    /// </summary>
    private void WriteStreamObject(int number, int generation, PdfDictionary source, ReadOnlyMemory<byte> data, ReadOnlySpan<byte> suffix, bool compressible, ReferenceWriter references, bool encrypt)
    {
        var dictionary = source.CloneShallow();
        var isShared = false;
        if (_options.CompressStreams && compressible && !dictionary.ContainsKey(PdfNames.Filter) && data.Length + suffix.Length > 0)
        {
            _compressed.SetLength(0);
            using (var zlib = new ZLibStream(_compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(data.Span);
                zlib.Write(suffix);
            }

            data = _compressed.GetBuffer().AsMemory(0, (int)_compressed.Length);
            suffix = [];
            isShared = true;
            dictionary[PdfNames.Filter] = PdfNames.FlateDecode;
        }

        // The data of a stream is encrypted after it is compressed, with the key of its number and generation (7.6.2)
        var security = encrypt ? _security : null;
        if (security is not null && security.GetStreamMethod(dictionary) is var method and not CryptMethod.None)
        {
            if (suffix.Length > 0)
            {
                var joined = new byte[data.Length + suffix.Length];
                data.Span.CopyTo(joined);
                suffix.CopyTo(joined.AsSpan(data.Length));
                data = joined;
                suffix = [];
            }

            data = security.EncryptStream(data.Span, method, number, generation);
            isShared = false;
        }

        dictionary[PdfNames.Length] = PdfInteger.Get(data.Length + suffix.Length);
        WriteObjectHeader(number, generation);
        PdfStringTransform? encryptString = security is null ? null : text => security.EncryptString(text, number, generation);
        PdfObjectSerializer.Write(_output, dictionary, references, encryptString);
        _output.WriteAscii("\nstream\n");
        if (isShared)
        {
            _output.Write(data.Span);
        }
        else
        {
            _output.Write(data);
        }

        _output.Write(suffix);
        _output.WriteAscii("\nendstream\nendobj\n");
    }

    private void WriteObjectHeader(int number, int generation)
    {
        _output.WriteInteger(number);
        _output.WriteSpace();
        _output.WriteInteger(generation);
        _output.WriteAscii(" obj\n");
    }

    /// <summary>Writes the objects in object streams of <see cref="ObjectsPerObjectStream"/> objects, numbered from <paramref name="firstNumber"/>.</summary>
    /// <returns>The number, the objects and the offset of each object stream, as it is written.</returns>
    private IEnumerable<(int Number, List<(int Number, PdfObject Value)> Batch, long Offset)> WriteObjectStreams(List<(int Number, PdfObject Value)> objects, int firstNumber, ReferenceWriter references)
    {
        var containerNumber = firstNumber;
        for (var start = 0; start < objects.Count; start += ObjectsPerObjectStream)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var batch = objects.GetRange(start, Math.Min(ObjectsPerObjectStream, objects.Count - start));
            var offset = _output.Position;
            WriteObjectStream(containerNumber, batch, references);
            yield return (containerNumber, batch, offset);
            containerNumber++;
        }
    }

    /// <summary>
    /// Writes an object stream (7.5.7): the pairs of numbers and offsets, then the objects. The two parts are written in
    /// buffers reused by every object stream of the save, and compressed or written one after the other, without
    /// joining them, except to encrypt them uncompressed.
    /// </summary>
    private void WriteObjectStream(int number, List<(int Number, PdfObject Value)> batch, ReferenceWriter references)
    {
        var header = _objectStreamHeader ??= new PdfByteWriter(1024);
        var body = _objectStreamBody ??= new PdfByteWriter(16 * 1024);
        header.Clear();
        body.Clear();
        foreach (var (objectNumber, value) in batch)
        {
            if (header.Length > 0)
            {
                header.WriteSpace();
            }

            header.WriteInteger(objectNumber);
            header.WriteSpace();
            header.WriteInteger(body.Length);
            PdfObjectSerializer.Write(body, value, references);
            body.WriteNewLine();
        }

        header.WriteNewLine();
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.ObjStm,
            [PdfNames.N] = PdfInteger.Get(batch.Count),
            [PdfNames.First] = PdfInteger.Get(header.Length),
        };

        // The header is a small buffer: it is copied so that the body, the large part, is not
        WriteStreamObject(number, 0, dictionary, header.WrittenSpan.ToArray(), body.WrittenSpan, compressible: true, references, encrypt: true);
    }

    private void WriteXrefStream(int number, PdfDictionary dictionary, List<(int Start, int Count)> ranges, (int Type, long Field2, long Field3)[] entries, ReferenceWriter references)
    {
        var width2 = BytesNeeded(entries.Max(e => e.Field2));
        var width3 = BytesNeeded(entries.Max(e => e.Field3));
        var entryLength = 1 + width2 + width3;
        var data = new byte[entries.Length * entryLength];
        for (var i = 0; i < entries.Length; i++)
        {
            var span = data.AsSpan(i * entryLength, entryLength);
            span[0] = (byte)entries[i].Type;
            WriteField(span.Slice(1, width2), entries[i].Field2);
            WriteField(span.Slice(1 + width2, width3), entries[i].Field3);
        }

        dictionary[PdfNames.Type] = PdfNames.XRef;
        dictionary[PdfNames.W] = PdfArray.FromNumbers(1, width2, width3);
        if (ranges.Count != 1 || ranges[0].Start != 0)
        {
            var index = new PdfArray();
            foreach (var (start, count) in ranges)
            {
                index.Add(PdfInteger.Get(start));
                index.Add(PdfInteger.Get(count));
            }

            dictionary[PdfNames.Index] = index;
        }

        // The cross-reference stream is not encrypted (7.6.2)
        WriteStreamObject(number, 0, dictionary, data, [], compressible: true, references, encrypt: false);
    }

    /// <summary>
    /// Creates the trailer: <c>/Size</c>, <c>/Root</c>, <c>/Info</c> and <c>/ID</c>, the other entries of the trailer of
    /// the document (private ones included, 7.5.6), then <c>/Encrypt</c> and <c>/Prev</c>.
    /// </summary>
    private static PdfDictionary CreateTrailer(int size, PdfObject? root, PdfObject? info, PdfArray id, List<KeyValuePair<PdfName, PdfObject>> others, PdfObject? encrypt, PdfObject? prev)
    {
        var dictionary = new PdfDictionary
        {
            [PdfNames.Size] = PdfInteger.Get(size),
            [PdfNames.Root] = root,
            [PdfNames.Info] = info,
            [PdfNames.ID] = id,
        };
        foreach (var (key, value) in others)
        {
            dictionary[key] = value;
        }

        dictionary[PdfNames.Encrypt] = encrypt;
        dictionary[PdfNames.Prev] = prev;
        return dictionary;
    }

    /// <summary>Gets the entries of the trailer the writer does not compute: the private ones, and the standard ones it has no rule for.</summary>
    private static List<KeyValuePair<PdfName, PdfObject>> GetOtherTrailerEntries(PdfDictionary trailer)
    {
        var result = new List<KeyValuePair<PdfName, PdfObject>>();
        foreach (var entry in trailer)
        {
            var key = entry.Key;
            if (key == PdfNames.Size || key == PdfNames.Root || key == PdfNames.Info || key == PdfNames.ID || key == PdfNames.Encrypt || IsCrossReferenceKey(key))
                continue;

            result.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Gets a value indicating whether an entry of a trailer describes its cross-reference section rather than the
    /// document: <c>/Prev</c>, <c>/XRefStm</c> and the entries of a cross-reference stream (7.5.8.2). They are not
    /// kept, even when the <c>/Type</c> of a damaged cross-reference stream is missing.
    /// </summary>
    public static bool IsCrossReferenceKey(PdfName key) => key == PdfNames.Prev || key == PdfNames.XRefStm || key == PdfNames.Type || key == PdfNames.W || key == PdfNames.Index || key == PdfNames.Length || key == PdfNames.Filter || key == PdfNames.DecodeParms || key == PdfNames.F || key == PdfNames.FFilter || key == PdfNames.FDecodeParms || key == PdfNames.DL;

    /// <summary>The file identifier (14.4): the first part is kept from the original file, the second part changes with each save.</summary>
    /// <param name="contentHash">The MD5 hash of the content written: the whole file, or the update.</param>
    /// <param name="isUpdate"><see langword="true"/> for an incremental update.</param>
    private PdfArray ComputeId(byte[] contentHash, bool isUpdate)
    {
        var hash = PdfString.FromBytes(contentHash, hexadecimal: true);
        var first = hash;
        if (_firstId is not null)
        {
            first = PdfString.FromBytes(_firstId, hexadecimal: true);
        }
        else if ((isUpdate || _document.Reader is not null) && GetOriginalFirstId() is { } original)
        {
            first = PdfString.FromBytes(original, hexadecimal: true);
        }

        return new PdfArray(first, hash);
    }

    /// <summary>Gets the first part of the identifier of the file the document was read from; empty for an encrypted file without identifier, whose keys use an empty one.</summary>
    private byte[]? GetOriginalFirstId()
    {
        if (_document.Reader is null)
            return null;

        if (_document.Trailer.GetResolved(PdfNames.ID) is PdfArray existing && existing.TryGetResolved<PdfString>(0, out var original))
            return original.Span.ToArray();

        return _document.Reader.Security is not null ? [] : null;
    }

    private void WriteTableEntry(long offset, int generation)
    {
        _output.WritePaddedInteger(offset, 10);
        _output.WriteSpace();
        _output.WritePaddedInteger(generation, 5);
        _output.WriteAscii(" n \n");
    }

    /// <summary>Writes a free entry (7.5.4), with no next free object: the free list of the earlier sections is not rewritten.</summary>
    private void WriteFreeTableEntry(int nextGeneration)
    {
        _output.WriteAscii("0000000000 ");
        _output.WritePaddedInteger(nextGeneration, 5);
        _output.WriteAscii(" f \n");
    }

    private void WriteStartXref(long offset)
    {
        _output.WriteAscii("startxref\n");
        _output.WriteInteger(offset);
        _output.WriteAscii("\n%%EOF\n");
    }

    private static void PushEntries(Stack<PdfObject> stack, PdfDictionary dictionary, bool skipLength)
    {
        var values = new List<PdfObject>(dictionary.Count);
        foreach (var entry in dictionary)
        {
            if (skipLength && entry.Key == PdfNames.Length)
                continue;

            values.Add(entry.Value);
        }

        for (var i = values.Count - 1; i >= 0; i--)
        {
            stack.Push(values[i]);
        }
    }

    private static List<(int Start, int Count)> GetRanges(IEnumerable<int> numbers)
    {
        var ranges = new List<(int Start, int Count)>();
        foreach (var number in numbers.Order())
        {
            if (ranges.Count > 0 && ranges[^1].Start + ranges[^1].Count == number)
            {
                ranges[^1] = (ranges[^1].Start, ranges[^1].Count + 1);
            }
            else
            {
                ranges.Add((number, 1));
            }
        }

        return ranges;
    }

    private static int BytesNeeded(long value)
    {
        var bytes = 1;
        while (bytes < 8 && value >= 1L << (bytes * 8))
        {
            bytes++;
        }

        return bytes;
    }

    private static void WriteField(Span<byte> destination, long value)
    {
        for (var i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = (byte)value;
            value >>= 8;
        }
    }
}
