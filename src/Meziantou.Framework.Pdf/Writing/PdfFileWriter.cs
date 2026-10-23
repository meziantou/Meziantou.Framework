using System.Security.Cryptography;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Security;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// Writes a document (ISO 32000-2, 7.5): a full save writes the objects reachable from the trailer, numbered in the
/// order they are reached; an incremental save (7.5.6) appends the objects that changed to the original file.
/// </summary>
internal sealed class PdfFileWriter
{
    private const int ObjectsPerObjectStream = 100;

    // A comment of bytes above 127 after the header tells transfer programs the file is binary (7.5.2)
    private static readonly byte[] BinaryMarker = [(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'];

    private readonly PdfDocument _document;
    private readonly PdfSaveOptions _options;
    private readonly PdfByteWriter _output;

    // The security handler that encrypts the strings and streams of the objects, and the first part of the file
    // identifier its keys depend on (7.6.3.3)
    private StandardSecurityHandler? _security;
    private byte[]? _firstId;

    private PdfFileWriter(PdfDocument document, PdfSaveOptions options, PdfByteWriter output)
    {
        _document = document;
        _options = options;
        _output = output;
    }

    public static void WriteFull(PdfDocument document, PdfSaveOptions options, PdfByteWriter output) => new PdfFileWriter(document, options, output).WriteFull();

    public static void WriteIncremental(PdfDocument document, PdfSaveOptions options, PdfByteWriter output) => new PdfFileWriter(document, options, output).WriteIncremental();

    private void WriteFull()
    {
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

        foreach (var key in new[] { PdfNames.Root, PdfNames.Info })
        {
            if (trailer[key] is { } value)
            {
                Visit(value);
            }
        }

        while (queue.Count > 0)
        {
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
        var useObjectStreams = _options.UseObjectStreams;
        var version = GetVersion(useObjectStreams);
        WriteHeader(version);

        var offsets = new long[ordered.Count + 1];
        var compressed = new List<(int Number, PdfObject Value)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var number = i + 1;
            var value = ordered[i].Value!;
            if (useObjectStreams && value is not PdfStream)
            {
                compressed.Add((number, value));
                continue;
            }

            offsets[number] = _output.Length;
            WriteIndirectObject(number, 0, value, references);
        }

        var nextNumber = ordered.Count + 1;
        var containers = new List<(int Number, int Index)>(new (int, int)[ordered.Count + 1]);
        var objectStreamOffsets = new List<(int Number, long Offset)>();
        for (var start = 0; start < compressed.Count; start += ObjectsPerObjectStream)
        {
            var batch = compressed.GetRange(start, Math.Min(ObjectsPerObjectStream, compressed.Count - start));
            var containerNumber = nextNumber++;
            for (var i = 0; i < batch.Count; i++)
            {
                containers[batch[i].Number] = (containerNumber, i);
            }

            objectStreamOffsets.Add((containerNumber, _output.Length));
            WriteObjectStream(containerNumber, batch, references);
        }

        // The encryption dictionary is not encrypted, and not in an object stream (7.5.7)
        PdfReference? encryptReference = null;
        long encryptOffset = 0;
        if (_security is not null)
        {
            encryptReference = new PdfReference(writerObjects, nextNumber++, 0);
            encryptOffset = _output.Length;
            WriteIndirectObject(encryptReference.ObjectNumber, 0, _security.Dictionary, references, encrypt: false);
        }

        var root = trailer[PdfNames.Root];
        var info = trailer[PdfNames.Info];
        var id = ComputeId(_output.WrittenSpan, isUpdate: false);
        if (useObjectStreams)
        {
            var xrefNumber = nextNumber;
            var size = xrefNumber + 1;
            var xrefOffset = _output.Length;
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

            var dictionary = CreateTrailer(size, root, info, id);
            dictionary[PdfNames.Encrypt] = encryptReference;
            WriteXrefStream(xrefNumber, dictionary, [(0, size)], entries, references);
            WriteStartXref(xrefOffset);
        }
        else
        {
            var size = nextNumber;
            var xrefOffset = _output.Length;
            _output.WriteAscii("xref\n0 ");
            _output.WriteInteger(size);
            _output.WriteNewLine();
            _output.WriteAscii("0000000000 65535 f \n");
            for (var number = 1; number < size; number++)
            {
                WriteTableEntry(number == encryptReference?.ObjectNumber ? encryptOffset : offsets[number], 0);
            }

            var dictionary = CreateTrailer(size, root, info, id);
            dictionary[PdfNames.Encrypt] = encryptReference;
            _output.WriteAscii("trailer\n");
            PdfObjectSerializer.Write(_output, dictionary, references);
            _output.WriteNewLine();
            WriteStartXref(xrefOffset);
        }
    }

    private void WriteIncremental()
    {
        var reader = _document.Reader ?? throw new InvalidOperationException("Only a document read from a file can be saved incrementally");
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
        var changed = objects.Entries
            .Where(e => e.State == IndirectObjectState.Loaded && (e.IsNew || e.OriginalFingerprint is null || !e.OriginalFingerprint.Matches(e.Value!)))
            .OrderBy(e => e.Reference.ObjectNumber)
            .ToList();

        var source = reader.Data.Span;
        _output.Write(source);
        if (source.Length > 0 && source[^1] is not ((byte)'\n' or (byte)'\r'))
        {
            _output.WriteNewLine();
        }

        var updateStart = _output.Length;
        var references = ReferenceWriter.Identity;
        var offsets = new List<(int Number, int Generation, long Offset)>();
        foreach (var entry in changed)
        {
            offsets.Add((entry.Reference.ObjectNumber, entry.Reference.Generation, _output.Length));
            WriteIndirectObject(entry.Reference.ObjectNumber, entry.Reference.Generation, entry.Value!, references);
        }

        var trailer = _document.Trailer;
        var id = ComputeId(_output.WrittenSpan[updateStart..], isUpdate: true);
        if (reader.LastXrefIsStream)
        {
            var xrefNumber = objects.MaxObjectNumber + 1;
            var size = xrefNumber + 1;
            var xrefOffset = _output.Length;
            offsets.Add((xrefNumber, 0, xrefOffset));
            var ranges = GetRanges(offsets.Select(o => o.Number));
            var entries = new (int Type, long Field2, long Field3)[offsets.Count];
            for (var i = 0; i < offsets.Count; i++)
            {
                entries[i] = (1, offsets[i].Offset, offsets[i].Generation);
            }

            var dictionary = CreateTrailer(size, trailer[PdfNames.Root], trailer[PdfNames.Info], id);
            CopyEncrypt(trailer, dictionary);
            dictionary[PdfNames.Prev] = PdfInteger.Get(reader.LastXrefOffset);
            WriteXrefStream(xrefNumber, dictionary, ranges, entries, references);
            WriteStartXref(xrefOffset);
        }
        else
        {
            var size = objects.MaxObjectNumber + 1;
            var xrefOffset = _output.Length;
            _output.WriteAscii("xref\n");
            var index = 0;
            foreach (var (start, count) in GetRanges(offsets.Select(o => o.Number)))
            {
                _output.WriteInteger(start);
                _output.WriteSpace();
                _output.WriteInteger(count);
                _output.WriteNewLine();
                for (var i = 0; i < count; i++, index++)
                {
                    WriteTableEntry(offsets[index].Offset, offsets[index].Generation);
                }
            }

            var dictionary = CreateTrailer(size, trailer[PdfNames.Root], trailer[PdfNames.Info], id);
            CopyEncrypt(trailer, dictionary);
            dictionary[PdfNames.Prev] = PdfInteger.Get(reader.LastXrefOffset);
            _output.WriteAscii("trailer\n");
            PdfObjectSerializer.Write(_output, dictionary, references);
            _output.WriteNewLine();
            WriteStartXref(xrefOffset);
        }
    }

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
        _output.WriteInteger(number);
        _output.WriteSpace();
        _output.WriteInteger(generation);
        _output.WriteAscii(" obj\n");

        // The strings and streams of an object are encrypted with the key of its number and generation (7.6.2)
        var security = encrypt ? _security : null;
        PdfStringTransform? encryptString = security is null ? null : text => security.EncryptString(text, number, generation);
        if (value is PdfStream stream)
        {
            var (dictionary, data) = PrepareStream(stream);
            if (security is not null && security.GetStreamMethod(dictionary) is var method and not CryptMethod.None)
            {
                data = security.EncryptStream(data.Span, method, number, generation);
                dictionary[PdfNames.Length] = PdfInteger.Get(data.Length);
            }

            PdfObjectSerializer.Write(_output, dictionary, references, encryptString);
            _output.WriteAscii("\nstream\n");
            _output.Write(data.Span);
            _output.WriteAscii("\nendstream");
        }
        else
        {
            PdfObjectSerializer.Write(_output, value, references, encryptString);
        }

        _output.WriteAscii("\nendobj\n");
    }

    /// <summary>Gets the dictionary and the data to write: the data compressed when allowed, the length direct.</summary>
    private (PdfDictionary Dictionary, ReadOnlyMemory<byte> Data) PrepareStream(PdfStream stream)
    {
        var dictionary = stream.Dictionary.CloneShallow();
        var data = stream.EncodedData;
        if (_options.CompressStreams && stream.IsCompressible && !dictionary.ContainsKey(PdfNames.Filter) && data.Length > 0)
        {
            data = FlateFilter.Encode(data.Span);
            dictionary[PdfNames.Filter] = PdfNames.FlateDecode;
        }

        dictionary[PdfNames.Length] = PdfInteger.Get(data.Length);
        return (dictionary, data);
    }

    private void WriteObjectStream(int number, List<(int Number, PdfObject Value)> batch, ReferenceWriter references)
    {
        var body = new PdfByteWriter();
        var header = new PdfByteWriter();
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
        var data = new byte[header.Length + body.Length];
        header.WrittenSpan.CopyTo(data);
        body.WrittenSpan.CopyTo(data.AsSpan(header.Length));

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.ObjStm,
            [PdfNames.N] = PdfInteger.Get(batch.Count),
            [PdfNames.First] = PdfInteger.Get(header.Length),
        };
        var stream = new PdfStream(dictionary, data);
        WriteIndirectObject(number, 0, stream, references);
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

        var stream = new PdfStream(dictionary, data);
        WriteIndirectObject(number, 0, stream, references, encrypt: false);
    }

    private static PdfDictionary CreateTrailer(int size, PdfObject? root, PdfObject? info, PdfArray id)
    {
        var dictionary = new PdfDictionary
        {
            [PdfNames.Size] = PdfInteger.Get(size),
            [PdfNames.Root] = root,
            [PdfNames.Info] = info,
            [PdfNames.ID] = id,
        };
        return dictionary;
    }

    private static void CopyEncrypt(PdfDictionary trailer, PdfDictionary dictionary)
    {
        if (trailer[PdfNames.Encrypt] is { } encrypt)
        {
            dictionary[PdfNames.Encrypt] = encrypt;
        }
    }

    /// <summary>The file identifier (14.4): the first part is kept from the original file, the second part changes with each save.</summary>
    private PdfArray ComputeId(ReadOnlySpan<byte> content, bool isUpdate)
    {
        var hash = PdfString.FromBytes(MD5.HashData(content), hexadecimal: true);
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
