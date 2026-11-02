using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// Writes a linearized file (ISO 32000-2, Annex F): the linearization parameter dictionary, the cross-reference table of
/// the first page, the catalog and the objects needed to open the document, the primary hint stream, the objects of the
/// first page, then the other pages, the objects they share and the other objects, and the main cross-reference table.
/// </summary>
internal sealed partial class PdfFileWriter
{
    private const int PaddedNumberWidth = 10;
    private const int SharedDenominator = 4;
    private const int MaxPageTreeDepth = 256;

    // The entries of the catalog the viewer needs to open the document (F.3.4)
    private static readonly PdfName[] OpenDocumentKeys = [PdfName.Create("ViewerPreferences"), PdfName.Create("PageMode"), PdfName.Create("Threads"), PdfName.Create("OpenAction"), PdfName.Create("AcroForm")];

    // The attributes a page inherits from the nodes of the page tree (7.7.3.4): written on the pages
    private static readonly PdfName[] InheritedKeys = [PdfName.Create("Resources"), PdfName.Create("MediaBox"), PdfName.Create("CropBox"), PdfName.Create("Rotate")];

    private static readonly PdfName OutlinesName = PdfName.Create("Outlines");
    private static readonly PdfName PageModeName = PdfName.Create("PageMode");
    private static readonly PdfName UseOutlinesName = PdfName.Create("UseOutlines");
    private static readonly PdfName KidsName = PdfName.Create("Kids");
    private static readonly PdfName ParentName = PdfName.Create("Parent");
    private static readonly PdfName PageName = PdfName.Create("Page");
    private static readonly PdfName PagesName = PdfName.Create("Pages");

    private enum LinearizedPart
    {
        Document,
        FirstPage,
        Page,
        Shared,
        PageTree,
        Outlines,
        Other,
    }

    private void WriteLinearized()
    {
        if (_document.Encryption is not null)
            throw new InvalidOperationException("A linearized file cannot be encrypted: save it without PdfSaveOptions.Linearize");

        var isPdfA1 = Conformance.PdfALevel.GetPart(_document.PdfAConformance) == 1;
        if (isPdfA1 && _options.Version is { } requested && requested != PdfVersion.Pdf14)
            throw new InvalidOperationException($"PDF/A-1 is PDF 1.4: the requested version is {requested}");

        var objects = _document.Objects;
        var trailer = _document.Trailer;
        var catalogReference = trailer[PdfNames.Root] as PdfReference ?? throw new InvalidOperationException("The catalog of a linearized file is an indirect object");
        var catalogEntry = objects.GetEntry(catalogReference) ?? throw new InvalidOperationException("The catalog of the document does not exist");
        var pages = _document.Pages.Select(page => objects.GetEntry(page.Reference) ?? throw new InvalidOperationException("A page of the document does not exist")).ToList();
        if (pages.Count == 0)
            throw new InvalidOperationException("A linearized file has at least one page");

        var overrides = PushInheritedAttributes(objects, catalogEntry);
        var graph = new LinearizationGraph(objects, overrides, _cancellationToken);

        // Who uses each object (F.3): the entries of the catalog that open the document, each page, the page tree, the
        // outline and the others. A traversal does not enter the pages it does not start from, nor the page tree.
        var catalog = (PdfDictionary)graph.GetValue(catalogEntry);
        var root = graph.Get(catalogEntry);
        root.IsDocument = true;
        var outlinesOpen = catalog.GetName(PageModeName) == UseOutlinesName;
        foreach (var (key, value) in catalog)
        {
            if (OpenDocumentKeys.Contains(key) || (outlinesOpen && key == OutlinesName))
            {
                graph.Traverse(value, o => o.IsDocument = true, startPage: null);
            }
        }

        var pageObjects = new List<LinearizedObject>[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var index = i;
            pageObjects[i] = [];
            graph.Traverse(pages[i].Reference, o =>
            {
                pageObjects[index].Add(o);
                if (index == 0)
                {
                    o.IsFirstPage = true;
                }
                else
                {
                    o.PageCount++;
                    if (o.PageIndex < 0)
                    {
                        o.PageIndex = index;
                    }
                }
            }, startPage: pages[i]);
        }

        if (catalog[PagesName] is PdfReference treeRoot)
        {
            graph.WalkPageTree(treeRoot);
        }

        if (!outlinesOpen && catalog[OutlinesName] is { } outlines)
        {
            graph.Traverse(outlines, o => o.IsOutline = true, startPage: null);
        }

        foreach (var (key, value) in catalog)
        {
            if (key != PagesName && key != OutlinesName && !OpenDocumentKeys.Contains(key))
            {
                graph.Traverse(value, o => o.IsOther = true, startPage: null);
            }
        }

        var info = OmitsInformation() ? null : trailer[PdfNames.Info];
        var others = GetOtherTrailerEntries(trailer);
        foreach (var value in new[] { info }.Concat(others.Select(e => e.Value)))
        {
            if (value is not null)
            {
                graph.Traverse(value, o => o.IsOther = true, startPage: null);
            }
        }

        // The parts of the file (F.2), each in the order the objects were reached; a page object comes first in its part
        var all = graph.Objects;
        foreach (var o in all)
        {
            o.Part = o == root || o.IsDocument ? LinearizedPart.Document
                : o.IsFirstPage ? LinearizedPart.FirstPage
                : o.PageCount == 1 ? LinearizedPart.Page
                : o.PageCount > 1 ? LinearizedPart.Shared
                : o.IsPageTree ? LinearizedPart.PageTree
                : o.IsOutline ? LinearizedPart.Outlines
                : LinearizedPart.Other;
        }

        var pageHeads = pages.Select(graph.Get).ToList();
        var documentPart = new List<LinearizedObject> { root };
        documentPart.AddRange(all.Where(o => o.Part == LinearizedPart.Document && o != root));
        var firstPagePart = new List<LinearizedObject> { pageHeads[0] };
        firstPagePart.AddRange(all.Where(o => o.Part == LinearizedPart.FirstPage && o != pageHeads[0]));
        var main = new List<LinearizedObject>();
        var pageRanges = new (int Start, int Count)[pages.Count];
        pageRanges[0] = (0, firstPagePart.Count);
        var privateObjects = all.Where(o => o.Part == LinearizedPart.Page).ToLookup(o => o.PageIndex);
        for (var i = 1; i < pages.Count; i++)
        {
            var start = main.Count;
            main.Add(pageHeads[i]);
            main.AddRange(privateObjects[i].Where(o => o != pageHeads[i]));
            pageRanges[i] = (start, main.Count - start);
        }

        var sharedStart = main.Count;
        main.AddRange(all.Where(o => o.Part == LinearizedPart.Shared));
        var sharedCount = main.Count - sharedStart;
        main.AddRange(all.Where(o => o.Part == LinearizedPart.PageTree));
        var outlinesStart = main.Count;
        main.AddRange(all.Where(o => o.Part == LinearizedPart.Outlines));
        var outlinesCount = main.Count - outlinesStart;
        main.AddRange(all.Where(o => o.Part == LinearizedPart.Other));

        // The numbers: the objects of the main cross-reference table from 1, then the objects of the first page's table
        var numbers = new Dictionary<IndirectObjectEntry, int>();
        for (var i = 0; i < main.Count; i++)
        {
            main[i].Number = i + 1;
        }

        var firstNumber = main.Count + 1;
        var linearizationNumber = firstNumber;
        var next = firstNumber + 1;
        foreach (var o in documentPart)
        {
            o.Number = next++;
        }

        var hintNumber = next++;
        foreach (var o in firstPagePart)
        {
            o.Number = next++;
        }

        var size = next;
        foreach (var o in all)
        {
            numbers.Add(o.Entry, o.Number);
        }

        // The objects, serialized once in the order of the file: the hint stream goes between the two blocks
        var references = new RenumberingReferenceWriter(numbers, objects);
        var body = new PdfByteWriter(64 * 1024);
        var output = _output;
        _output = body;
        try
        {
            foreach (var o in documentPart.Concat(firstPagePart).Concat(main))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                o.Start = body.Position;
                WriteIndirectObject(o.Number, 0, o.Value, references);
                o.Length = body.Position - o.Start;
            }
        }
        finally
        {
            _output = output;
        }

        var documentEnd = firstPagePart[0].Start;
        var firstPageEnd = main.Count > 0 ? main[0].Start : body.Position;
        var id = ComputeId(MD5.HashData(body.WrittenSpan), isUpdate: false);

        // The fixed-width parts before the objects: the numbers that depend on the layout are padded
        var version = GetVersion(useObjectStreams: false);
        var header = Encoding.ASCII.GetBytes("%PDF-" + version.ToString() + "\n").Concat(BinaryMarker).ToArray();
        var pageCount = pages.Count;
        var page0Number = firstPagePart[0].Number;
        byte[] LinearizationDictionary(long length, long hintOffset, long hintLength, long end, long xrefOffset) => Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"{linearizationNumber} 0 obj\n<< /Linearized 1 /L {Pad(length)} /H [ {Pad(hintOffset)} {Pad(hintLength)} ] /O {page0Number} /E {Pad(end)} /N {pageCount} /T {Pad(xrefOffset)} >>\nendobj\n"));

        var linearizationLength = LinearizationDictionary(0, 0, 0, 0, 0).Length;
        var firstTrailer = new PdfByteWriter(256);
        PdfObjectSerializer.Write(firstTrailer, CreateTrailer(size, catalogReference, info, id, others, encrypt: null, prev: null), references);
        byte[] FirstSection(long prev, Func<int, long> offsetOf)
        {
            var section = new PdfByteWriter(1024);
            section.WriteAscii("xref\n");
            section.WriteInteger(firstNumber);
            section.WriteSpace();
            section.WriteInteger(size - firstNumber);
            section.WriteNewLine();
            for (var number = firstNumber; number < size; number++)
            {
                section.WritePaddedInteger(offsetOf(number), 10);
                section.WriteAscii(" 00000 n \n");
            }

            section.WriteAscii("trailer\n");
            section.Write(firstTrailer.WrittenSpan[..^2]);
            section.WriteAscii(" /Prev ");
            section.WriteAscii(Pad(prev));
            section.WriteAscii(">>\nstartxref\n0\n%%EOF\n");
            return section.ToArray();
        }

        var firstSectionLength = FirstSection(0, _ => 0).Length;
        var bodyStart = header.Length + linearizationLength + firstSectionLength;

        // The offsets of the hint tables are those of a file without the hint stream (F.4)
        long Adjusted(LinearizedObject o) => bodyStart + o.Start;
        var mainXrefAdjusted = bodyStart + body.Position;
        var mainXrefHeader = string.Create(CultureInfo.InvariantCulture, $"xref\n0 {firstNumber}");
        var hintData = CreateHintTables(firstPagePart, main, pageObjects, pageRanges, sharedStart, sharedCount, outlinesStart, outlinesCount, Adjusted, out var sharedOffset, out var outlinesOffset);

        var hintDictionary = new PdfDictionary { [PdfName.Create("S")] = PdfInteger.Get(sharedOffset) };
        if (outlinesOffset >= 0)
        {
            hintDictionary[PdfName.Create("O")] = PdfInteger.Get(outlinesOffset);
        }

        var hint = new PdfByteWriter(hintData.Length + 128);
        _output = hint;
        try
        {
            WriteStreamObject(hintNumber, 0, hintDictionary, hintData, [], compressible: true, references, encrypt: false);
        }
        finally
        {
            _output = output;
        }

        var hintLength = hint.Length;
        var hintOffset = bodyStart + documentEnd;
        long Actual(LinearizedObject o) => Adjusted(o) + (o.Start >= documentEnd ? hintLength : 0);
        var mainXrefOffset = mainXrefAdjusted + hintLength;

        var mainSection = new PdfByteWriter(64 + (20 * firstNumber));
        mainSection.WriteAscii(mainXrefHeader);
        mainSection.WriteAscii("\n0000000000 65535 f \n");
        foreach (var o in main)
        {
            WriteTableEntry(mainSection, Actual(o));
        }

        mainSection.WriteAscii("trailer\n");
        PdfObjectSerializer.Write(mainSection, new PdfDictionary { [PdfNames.Size] = PdfInteger.Get(firstNumber), [PdfNames.ID] = id }, references);
        mainSection.WriteAscii("\nstartxref\n");
        mainSection.WriteInteger(header.Length + linearizationLength);
        mainSection.WriteAscii("\n%%EOF\n");

        var fileLength = mainXrefOffset + mainSection.Length;
        var byNumber = documentPart.Concat(firstPagePart).ToDictionary(o => o.Number);
        var linearization = LinearizationDictionary(fileLength, hintOffset, hintLength, bodyStart + firstPageEnd + hintLength, mainXrefOffset + mainXrefHeader.Length);
        var firstSection = FirstSection(mainXrefOffset, number => number == linearizationNumber ? header.Length : number == hintNumber ? hintOffset : Actual(byNumber[number]));

        _output.Write(header);
        _output.Write(linearization);
        _output.Write(firstSection);
        _output.Write(body.WrittenSpan[..(int)documentEnd]);
        _output.Write(hint.WrittenSpan);
        _output.Write(body.WrittenSpan[(int)documentEnd..]);
        _output.Write(mainSection.WrittenSpan);
    }

    /// <summary>
    /// Creates the page offset hint table (F.4.1), the shared object hint table (F.4.2) and, when the outline is in the
    /// part of the other objects, the outline hint table (F.4.3, a generic hint table).
    /// </summary>
    private static byte[] CreateHintTables(List<LinearizedObject> firstPagePart, List<LinearizedObject> main, List<LinearizedObject>[] pageObjects, (int Start, int Count)[] pageRanges, int sharedStart, int sharedCount, int outlinesStart, int outlinesCount, Func<LinearizedObject, long> adjusted, out int sharedOffset, out int outlinesOffset)
    {
        // The shared objects: every object of the first page's part, then the objects the other pages share
        var sharedIdentifiers = new Dictionary<LinearizedObject, int>();
        var groups = new List<LinearizedObject>();
        foreach (var o in firstPagePart.Concat(main.GetRange(sharedStart, sharedCount)))
        {
            sharedIdentifiers.Add(o, groups.Count);
            groups.Add(o);
        }

        var pageCount = pageRanges.Length;
        var objectCounts = new long[pageCount];
        var lengths = new long[pageCount];
        var shared = new List<int>[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            var part = i == 0 ? firstPagePart.GetRange(0, firstPagePart.Count) : main.GetRange(pageRanges[i].Start, pageRanges[i].Count);
            objectCounts[i] = part.Count;
            lengths[i] = part.Sum(o => o.Length);

            // The first page refers to no shared object: its part holds every object it needs
            shared[i] = i == 0 ? [] : [.. pageObjects[i].Where(o => o.Part is LinearizedPart.FirstPage or LinearizedPart.Shared).Select(o => sharedIdentifiers[o]).Order()];
        }

        var bits = new BitWriter();
        var minObjects = objectCounts.Min();
        var minLength = lengths.Min();
        var objectBits = BitsNeeded(objectCounts.Max() - minObjects);
        var lengthBits = BitsNeeded(lengths.Max() - minLength);
        var sharedCountBits = BitsNeeded(shared.Max(s => s.Count));
        var identifierBits = BitsNeeded(groups.Count);
        Write32(bits, minObjects);
        Write32(bits, adjusted(firstPagePart[0]));
        Write16(bits, objectBits);
        Write32(bits, minLength);
        Write16(bits, lengthBits);

        // The offsets of the content streams are not given; their lengths are those of the pages, as viewers expect
        Write32(bits, 0);
        Write16(bits, 0);
        Write32(bits, minLength);
        Write16(bits, lengthBits);
        Write16(bits, sharedCountBits);
        Write16(bits, identifierBits);
        Write16(bits, 0);
        Write16(bits, SharedDenominator);
        WriteItems(bits, objectCounts.Select(c => c - minObjects), objectBits);
        WriteItems(bits, lengths.Select(l => l - minLength), lengthBits);
        WriteItems(bits, shared.Select(s => (long)s.Count), sharedCountBits);
        WriteItems(bits, shared.SelectMany(s => s).Select(i => (long)i), identifierBits);
        WriteItems(bits, lengths.Select(l => l - minLength), lengthBits);
        var pageTable = bits.ToArray();
        sharedOffset = pageTable.Length;

        var minGroup = groups.Min(o => o.Length);
        var groupBits = BitsNeeded(groups.Max(o => o.Length) - minGroup);
        var sharedBits = new BitWriter();
        Write32(sharedBits, sharedCount > 0 ? main[sharedStart].Number : 0);
        Write32(sharedBits, sharedCount > 0 ? adjusted(main[sharedStart]) : 0);
        Write32(sharedBits, firstPagePart.Count);
        Write32(sharedBits, groups.Count);
        Write16(sharedBits, 0);
        Write32(sharedBits, minGroup);
        Write16(sharedBits, groupBits);
        WriteItems(sharedBits, groups.Select(o => o.Length - minGroup), groupBits);
        WriteItems(sharedBits, groups.Select(_ => 0L), 1);
        var sharedTable = sharedBits.ToArray();

        outlinesOffset = -1;
        byte[] outlineTable = [];
        if (outlinesCount > 0)
        {
            outlinesOffset = pageTable.Length + sharedTable.Length;
            var outlines = main.GetRange(outlinesStart, outlinesCount);
            var outlineBits = new BitWriter();
            Write32(outlineBits, outlines[0].Number);
            Write32(outlineBits, adjusted(outlines[0]));
            Write32(outlineBits, outlines.Count);
            Write32(outlineBits, outlines.Sum(o => o.Length));
            outlineTable = outlineBits.ToArray();
        }

        return [.. pageTable, .. sharedTable, .. outlineTable];
    }

    /// <summary>Writes the attributes the pages inherit on the pages, and removes them from the nodes of the page tree (7.7.3.4).</summary>
    private static Dictionary<IndirectObjectEntry, PdfObject> PushInheritedAttributes(PdfIndirectObjectCollection objects, IndirectObjectEntry catalogEntry)
    {
        var overrides = new Dictionary<IndirectObjectEntry, PdfObject>();
        var visited = new HashSet<IndirectObjectEntry>();
        void Visit(PdfReference reference, Dictionary<PdfName, PdfObject> inherited, int depth)
        {
            if (depth > MaxPageTreeDepth || !ReferenceEquals(reference.Owner, objects) || objects.GetEntry(reference) is not { } entry || !visited.Add(entry) || objects.Load(entry) is not PdfDictionary node)
                return;

            if (node.IsType(PagesName))
            {
                var values = new Dictionary<PdfName, PdfObject>(inherited);
                PdfDictionary? clone = null;
                foreach (var key in InheritedKeys)
                {
                    if (node[key] is { } value)
                    {
                        values[key] = value;
                        clone ??= node.CloneShallow();
                        clone.Remove(key);
                    }
                }

                if (clone is not null)
                {
                    overrides[entry] = clone;
                }

                if (node.GetResolved(KidsName) is PdfArray kids)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfReference kidReference)
                        {
                            Visit(kidReference, values, depth + 1);
                        }
                    }
                }
            }
            else
            {
                PdfDictionary? clone = null;
                foreach (var (key, value) in inherited)
                {
                    if (!node.ContainsKey(key))
                    {
                        clone ??= node.CloneShallow();
                        clone[key] = value;
                    }
                }

                if (clone is not null)
                {
                    overrides[entry] = clone;
                }
            }
        }

        if (objects.Load(catalogEntry) is PdfDictionary catalog && catalog[PagesName] is PdfReference pages)
        {
            Visit(pages, [], 0);
        }

        return overrides;
    }

    private static string Pad(long value) => value.ToString(CultureInfo.InvariantCulture).PadRight(PaddedNumberWidth);

    private static void WriteTableEntry(PdfByteWriter writer, long offset)
    {
        writer.WritePaddedInteger(offset, 10);
        writer.WriteAscii(" 00000 n \n");
    }

    private static int BitsNeeded(long value)
    {
        var bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }

        return bits;
    }

    private static void Write32(BitWriter writer, long value) => writer.Write(checked((uint)value), 32);

    private static void Write16(BitWriter writer, int value) => writer.Write(checked((uint)value), 16);

    /// <summary>Writes one item of a hint table for every page or group, then pads it to a byte (F.4.1).</summary>
    private static void WriteItems(BitWriter writer, IEnumerable<long> values, int bits)
    {
        if (bits > 0)
        {
            foreach (var value in values)
            {
                writer.Write(checked((uint)value), bits);
            }
        }

        writer.Flush();
    }

    /// <summary>An object of a linearized file: who uses it, its part, its number and where it is.</summary>
    private sealed class LinearizedObject(IndirectObjectEntry entry, PdfObject value)
    {
        public IndirectObjectEntry Entry { get; } = entry;

        public PdfObject Value { get; } = value;

        public bool IsDocument { get; set; }

        public bool IsFirstPage { get; set; }

        public bool IsPageTree { get; set; }

        public bool IsOutline { get; set; }

        public bool IsOther { get; set; }

        /// <summary>Gets or sets the number of pages other than the first that use the object.</summary>
        public int PageCount { get; set; }

        /// <summary>Gets or sets the first page other than the first that uses the object, or -1.</summary>
        public int PageIndex { get; set; } = -1;

        public LinearizedPart Part { get; set; }

        public int Number { get; set; }

        public long Start { get; set; }

        public long Length { get; set; }
    }

    /// <summary>The objects reached from the parts of the document, in the order they are reached.</summary>
    private sealed class LinearizationGraph(PdfIndirectObjectCollection objects, Dictionary<IndirectObjectEntry, PdfObject> overrides, CancellationToken cancellationToken)
    {
        private readonly Dictionary<IndirectObjectEntry, LinearizedObject> _objects = [];

        public List<LinearizedObject> Objects { get; } = [];

        public PdfObject GetValue(IndirectObjectEntry entry) => overrides.GetValueOrDefault(entry) ?? objects.Load(entry);

        public LinearizedObject Get(IndirectObjectEntry entry)
        {
            if (!_objects.TryGetValue(entry, out var result))
            {
                result = new LinearizedObject(entry, GetValue(entry));
                _objects.Add(entry, result);
                Objects.Add(result);
            }

            return result;
        }

        /// <summary>Marks the objects reached from a value, without entering a page other than <paramref name="startPage"/> or a node of the page tree.</summary>
        public void Traverse(PdfObject start, Action<LinearizedObject> mark, IndirectObjectEntry? startPage)
        {
            var visited = new HashSet<IndirectObjectEntry>();
            var stack = new Stack<PdfObject>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                switch (stack.Pop())
                {
                    case PdfReference reference:
                        if (!ReferenceEquals(reference.Owner, objects) || objects.GetEntry(reference) is not { } entry || !visited.Add(entry))
                            break;

                        var value = GetValue(entry);
                        if (value is PdfNull || (value is PdfDictionary target && ((target.IsType(PageName) && entry != startPage) || target.IsType(PagesName))))
                            break;

                        cancellationToken.ThrowIfCancellationRequested();
                        mark(Get(entry));
                        stack.Push(value);
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

        /// <summary>Marks the nodes of the page tree, and the objects their other entries use.</summary>
        public void WalkPageTree(PdfReference root)
        {
            var visited = new HashSet<IndirectObjectEntry>();
            var stack = new Stack<(PdfReference Reference, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (reference, depth) = stack.Pop();
                if (depth > MaxPageTreeDepth || !ReferenceEquals(reference.Owner, objects) || objects.GetEntry(reference) is not { } entry || !visited.Add(entry) || GetValue(entry) is not PdfDictionary node || !node.IsType(PagesName))
                    continue;

                Get(entry).IsPageTree = true;
                foreach (var (key, value) in node)
                {
                    if (key == KidsName)
                    {
                        var kids = PdfReference.ResolveIfReference(value) as PdfArray;
                        for (var i = (kids?.Count ?? 0) - 1; i >= 0; i--)
                        {
                            if (kids![i] is PdfReference kid)
                            {
                                stack.Push((kid, depth + 1));
                            }
                        }
                    }
                    else if (key != ParentName)
                    {
                        Traverse(value, o => o.IsOther = true, startPage: null);
                    }
                }
            }
        }
    }
}
