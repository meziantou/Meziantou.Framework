using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Avif;

/// <summary>
/// An independent, structure-only reader of AVIF files, written from the specifications for the tests and for the corpus
/// generator: the boxes of ISO/IEC 14496-12 (file type, <c>meta</c> and its item boxes, <c>moov</c> and its sample tables),
/// the image item model of ISO/IEC 23008-12 (item properties, <c>grid</c> derived images, auxiliary images, <c>Exif</c> and
/// XMP items, transformative properties) and the AV1 Image File Format rules (the <c>av1C</c> configuration record, alpha
/// auxiliary images and tracks, image sequences). It never uses the library under test and never decodes AV1: the coded
/// payloads are returned as bytes.
/// </summary>
/// <remarks>
/// The reader is random access (the whole file is in memory), unlike the forward-only walker of the library, so that both
/// do not share a design. It reads every item and every track of the file, whether or not a decoder would use them.
/// </remarks>
public sealed class ReferenceAvif
{
    /// <summary>The auxiliary type of an alpha plane (ISO/IEC 23008-12, section 6.6.2.1.2).</summary>
    public const string AlphaAuxiliaryType = "urn:mpeg:mpegB:cicp:systems:auxiliary:alpha";

    private readonly byte[] _data;
    private readonly List<Box> _topLevelBoxes = [];
    private readonly List<string> _compatibleBrands = [];
    private readonly List<Track> _tracks = [];
    private MetaContent _meta = new();

    private ReferenceAvif(byte[] data)
    {
        _data = data;
    }

    /// <summary>Gets the major brand of the file type box.</summary>
    public string MajorBrand { get; private set; } = "";

    /// <summary>Gets the compatible brands of the file type box, in order.</summary>
    public IReadOnlyList<string> CompatibleBrands => _compatibleBrands;

    /// <summary>Gets the top-level boxes, in file order.</summary>
    public IReadOnlyList<Box> TopLevelBoxes => _topLevelBoxes;

    /// <summary>Gets a value indicating whether the file has a <c>meta</c> box.</summary>
    public bool HasMeta { get; private set; }

    /// <summary>Gets the identifier of the primary item (<c>pitm</c>), or <see langword="null"/>.</summary>
    public uint? PrimaryItemId => _meta.PrimaryItemId;

    /// <summary>Gets the items of the <c>meta</c> box of the file, ordered by identifier.</summary>
    public IReadOnlyList<Item> Items => _meta.Items;

    /// <summary>Gets the item references (<c>iref</c>), in order.</summary>
    public IReadOnlyList<ItemReference> ItemReferences => _meta.References;

    /// <summary>Gets the item properties (<c>ipco</c>), in order: an association refers to them by their 1-based index.</summary>
    public IReadOnlyList<Property> Properties => _meta.Properties;

    /// <summary>Gets the payload of the <c>idat</c> box of the <c>meta</c> box of the file, or an empty buffer.</summary>
    public ReadOnlyMemory<byte> ItemData => _meta.ItemData;

    /// <summary>Gets the time scale of the movie (<c>mvhd</c>), 0 without <c>moov</c> box.</summary>
    public uint MovieTimescale { get; private set; }

    /// <summary>Gets the tracks of the <c>moov</c> box, in order.</summary>
    public IReadOnlyList<Track> Tracks => _tracks;

    /// <summary>Gets a value indicating whether the file has a movie fragment box (<c>moof</c>).</summary>
    public bool IsFragmented => _topLevelBoxes.Any(box => box.Type == "moof");

    /// <summary>Gets a value indicating whether the file declares the brand of the image sequences (<c>avis</c>).</summary>
    public bool IsSequence => HasBrand("avis");

    /// <summary>Gets the primary item, or <see langword="null"/> when the file has none.</summary>
    public Item? PrimaryItem => PrimaryItemId is { } id ? FindItem(id) : null;

    /// <summary>Gets the alpha auxiliary image of the primary item, or <see langword="null"/>.</summary>
    public Item? AlphaItem => PrimaryItemId is { } id ? FindAuxiliary(id, AlphaAuxiliaryType) : null;

    /// <summary>
    /// Gets a value indicating whether the colors of the image that the file presents are multiplied by the alpha of their
    /// pixel: a <c>prem</c> reference from the primary item to its alpha plane (ISO/IEC 23008-12, section 6.6.2.2), or
    /// from the color track of an image sequence to its alpha track.
    /// </summary>
    public bool IsPremultiplied
    {
        get
        {
            if (IsSequence)
                return ColorTrack is { } color && AlphaTrack is { } alphaTrack && color.References.Any(reference => reference.Type == "prem" && reference.TrackIds.Contains(alphaTrack.Id));

            return PrimaryItemId is { } id && AlphaItem is { } alpha && ItemReferences.Any(reference => reference.Type == "prem" && reference.FromItemId == id && reference.ToItemIds.Contains(alpha.Id));
        }
    }

    /// <summary>Gets the first <c>pict</c> track that has AV1 samples: the image sequence of the file, or <see langword="null"/>.</summary>
    public Track? ColorTrack => _tracks.FirstOrDefault(track => track.HandlerType == "pict" && track.SampleEntryType == "av01" && track.Samples.Count > 0);

    /// <summary>Gets the alpha track of <see cref="ColorTrack"/>: an AV1 track that is auxiliary to it, or <see langword="null"/>.</summary>
    public Track? AlphaTrack
    {
        get
        {
            var color = ColorTrack;
            if (color is null)
                return null;

            return _tracks.FirstOrDefault(track => !ReferenceEquals(track, color) && track.SampleEntryType == "av01"
                && track.References.Any(reference => reference.Type == "auxl" && reference.TrackIds.Contains(color.Id))
                && track.AuxiliaryType is null or AlphaAuxiliaryType);
        }
    }

    /// <summary>Parses an AVIF file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="InvalidDataException">A box does not fit its container or is too short for its fields.</exception>
    public static ReferenceAvif Parse(ReadOnlySpan<byte> data)
    {
        var result = new ReferenceAvif(data.ToArray());
        result.ReadTopLevel();
        return result;
    }

    /// <summary>Gets a value indicating whether a brand is the major brand or one of the compatible brands.</summary>
    /// <param name="brand">The four-character code.</param>
    /// <returns><see langword="true"/> when the file declares the brand.</returns>
    public bool HasBrand(string brand) => MajorBrand == brand || _compatibleBrands.Contains(brand, StringComparer.Ordinal);

    /// <summary>Gets an item.</summary>
    /// <param name="id">The item identifier.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Item? FindItem(uint id) => _meta.Find(id);

    /// <summary>Gets the first auxiliary image of an item that has an auxiliary type (<c>auxl</c> reference and <c>auxC</c> property).</summary>
    /// <param name="itemId">The item the auxiliary image refers to.</param>
    /// <param name="auxiliaryType">The auxiliary type URN.</param>
    /// <returns>The auxiliary item, or <see langword="null"/>.</returns>
    public Item? FindAuxiliary(uint itemId, string auxiliaryType)
    {
        foreach (var reference in _meta.References)
        {
            if (reference.Type == "auxl" && reference.ToItemIds.Contains(itemId) && FindItem(reference.FromItemId) is { } item && item.AuxiliaryType == auxiliaryType)
                return item;
        }

        return null;
    }

    /// <summary>Gets the items that describe an item (<c>cdsc</c> references to it), in reference order.</summary>
    /// <param name="itemId">The described item.</param>
    /// <returns>The describing items.</returns>
    public IReadOnlyList<Item> FindDescriptions(uint itemId)
        => [.. _meta.References.Where(reference => reference.Type == "cdsc" && reference.ToItemIds.Contains(itemId)).Select(reference => FindItem(reference.FromItemId)).OfType<Item>()];

    /// <summary>Gets the tiles of a <c>grid</c> item (<c>dimg</c> reference), in row-major order.</summary>
    /// <param name="grid">The grid item.</param>
    /// <returns>The tile items.</returns>
    /// <exception cref="InvalidDataException">The grid has no tile list, or a tile does not exist.</exception>
    public IReadOnlyList<Item> GetGridTiles(Item grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var reference = _meta.References.FirstOrDefault(candidate => candidate.Type == "dimg" && candidate.FromItemId == grid.Id) ?? throw new InvalidDataException("The grid has no 'dimg' reference.");
        return [.. reference.ToItemIds.Select(id => FindItem(id) ?? throw new InvalidDataException("A tile of the grid does not exist."))];
    }

    /// <summary>Reads the descriptor of a <c>grid</c> item (ISO/IEC 23008-12, section 6.6.2.3.2).</summary>
    /// <param name="grid">The grid item.</param>
    /// <returns>The number of rows and columns and the size of the reconstructed image.</returns>
    /// <exception cref="InvalidDataException">The descriptor is malformed.</exception>
    public (int Rows, int Columns, long Width, long Height) ReadGridDescriptor(Item grid)
    {
        var data = ReadItemData(grid);
        if (data.Length < 8 || data[0] != 0)
            throw new InvalidDataException("The grid descriptor is not valid.");

        var large = (data[1] & 1) != 0;
        if (data.Length != (large ? 12 : 8))
            throw new InvalidDataException("The grid descriptor does not have the length of its fields.");

        return large
            ? (data[2] + 1, data[3] + 1, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)), BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)))
            : (data[2] + 1, data[3] + 1, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6)));
    }

    /// <summary>
    /// Finds the <c>Exif</c> and XMP items of the image sequence of the file, as the library documents them: the items of the
    /// <c>meta</c> box of the track of the color images when the track has one (where libavif writes and reads them);
    /// otherwise the items of the <c>meta</c> box of the file when that box precedes the <c>moov</c> box, the order in which
    /// a forward-only reader meets them. In the box that applies, the <c>Exif</c> item and the XMP item (a <c>mime</c> item
    /// of content type <c>application/rdf+xml</c>) of lowest identifier are the metadata of the sequence, whatever they refer to.
    /// </summary>
    /// <returns>The items, each of them <see langword="null"/> when the sequence has none.</returns>
    public (Item? Exif, Item? Xmp) FindSequenceMetadata()
    {
        IReadOnlyList<Item> items;
        if (ColorTrack is { HasMeta: true } track)
        {
            items = track.MetaItems;
        }
        else
        {
            var meta = _topLevelBoxes.FindIndex(box => box.Type == "meta");
            var movie = _topLevelBoxes.FindIndex(box => box.Type == "moov");
            if (ColorTrack is null || meta < 0 || meta > movie)
                return (null, null);

            items = Items;
        }

        return (items.FirstOrDefault(item => item.Type == "Exif"), items.FirstOrDefault(item => item.Type == "mime" && item.ContentType == "application/rdf+xml"));
    }

    /// <summary>Reads the data of an item: its extents, in order, from the file or from the <c>idat</c> box of its <c>meta</c> box.</summary>
    /// <param name="item">The item, of the <c>meta</c> box of the file or of a track.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidDataException">An extent is outside its source, or the construction method is not 0 or 1.</exception>
    public byte[] ReadItemData(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ConstructionMethod > 1)
            throw new InvalidDataException("The item is constructed from other items.");

        ReadOnlySpan<byte> source = item.ConstructionMethod == 1 ? item.ItemDataBox.Span : _data;
        using var output = new MemoryStream();
        foreach (var (offset, length) in item.Extents)
        {
            // A length of 0 is the whole source from the offset (ISO/IEC 14496-12, section 8.11.3.3)
            var extentLength = length == 0 ? source.Length - offset : length;
            if (offset < 0 || offset > source.Length || extentLength < 0 || extentLength > source.Length - offset)
                throw new InvalidDataException("An item extent is outside its source.");

            output.Write(source.Slice((int)offset, (int)extentLength));
        }

        return output.ToArray();
    }

    /// <summary>Reads the Exif payload of an <c>Exif</c> item: the TIFF data that follow the offset field (ISO/IEC 23008-12, annex A.2.1).</summary>
    /// <param name="item">The <c>Exif</c> item.</param>
    /// <returns>The data from the TIFF header.</returns>
    /// <exception cref="InvalidDataException">The item is too short for its offset field.</exception>
    public byte[] ReadExifPayload(Item item)
    {
        var data = ReadItemData(item);
        if (data.Length < 4)
            throw new InvalidDataException("The Exif item has no offset field.");

        var offset = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (offset > data.Length - 4)
            throw new InvalidDataException("The Exif offset is outside the item.");

        return data[(4 + (int)offset)..];
    }

    /// <summary>Reads the bytes of a sample of a track.</summary>
    /// <param name="sample">The sample.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidDataException">The sample is outside the file.</exception>
    public byte[] ReadSample(Sample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Offset < 0 || sample.Offset > _data.Length || sample.Size > _data.Length - sample.Offset)
            throw new InvalidDataException("A sample is outside the file.");

        return _data.AsSpan((int)sample.Offset, sample.Size).ToArray();
    }

    /// <summary>
    /// Computes the EXIF orientation that is equivalent to transformations applied in order (ISO/IEC 23008-12, sections
    /// 6.5.10 and 6.5.12): <c>irot</c> rotates anti-clockwise by a multiple of 90 degrees, <c>imir</c> exchanges the top and
    /// the bottom (axis 0) or the left and the right (axis 1).
    /// </summary>
    /// <param name="transforms">The transformative properties, in association order; <c>clap</c> is ignored.</param>
    /// <returns>The EXIF orientation value, 1 to 8.</returns>
    public static int ComposeOrientation(IEnumerable<Property> transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);

        // A transformation maps the offset (x, y) of a stored sample from the center of the image, y pointing down, to its
        // displayed offset: the matrix (a b; c d) of x' = a x + b y, y' = c x + d y
        (int A, int B, int C, int D) matrix = (1, 0, 0, 1);
        foreach (var property in transforms)
        {
            (int A, int B, int C, int D) step;
            if (property.Type == "irot")
            {
                // A quarter turn anti-clockwise moves the right of the image to its top: (1, 0) -> (0, -1), (0, 1) -> (1, 0)
                step = (property.Payload.Span[0] & 3) switch
                {
                    1 => (0, 1, -1, 0),
                    2 => (-1, 0, 0, -1),
                    3 => (0, -1, 1, 0),
                    _ => (1, 0, 0, 1),
                };
            }
            else if (property.Type == "imir")
            {
                step = (property.Payload.Span[0] & 1) == 0 ? (1, 0, 0, -1) : (-1, 0, 0, 1);
            }
            else
            {
                continue;
            }

            // The step applies after the transformations so far
            matrix = ((step.A * matrix.A) + (step.B * matrix.C), (step.A * matrix.B) + (step.B * matrix.D), (step.C * matrix.A) + (step.D * matrix.C), (step.C * matrix.B) + (step.D * matrix.D));
        }

        // The EXIF orientations as the transformation that displays the stored samples (TIFF 6.0, tag 274)
        return matrix switch
        {
            (1, 0, 0, 1) => 1,
            (-1, 0, 0, 1) => 2,
            (-1, 0, 0, -1) => 3,
            (1, 0, 0, -1) => 4,
            (0, 1, 1, 0) => 5,
            (0, -1, 1, 0) => 6,
            (0, -1, -1, 0) => 7,
            (0, 1, -1, 0) => 8,
            _ => throw new InvalidOperationException("The composition of quarter turns and mirrors is one of eight orientations."),
        };
    }

    private void ReadTopLevel()
    {
        var offset = 0L;
        while (offset < _data.Length)
        {
            var box = ReadBoxHeader(_data, offset, _data.Length);
            _topLevelBoxes.Add(box);
            var payload = _data.AsMemory((int)(box.Offset + box.HeaderLength), (int)(box.Size - box.HeaderLength));
            switch (box.Type)
            {
                case "ftyp":
                    ReadFileType(payload.Span);
                    break;
                case "meta" when !HasMeta:
                    HasMeta = true;
                    _meta = ReadMeta(payload);
                    break;
                case "moov" when MovieTimescale == 0 && _tracks.Count == 0:
                    ReadMovie(payload);
                    break;
                default:
                    break;
            }

            offset += box.Size;
        }
    }

    // ISO/IEC 14496-12, section 4.2 "Object Structure": size, type, a 64-bit size when size is 1; size 0 extends to the end
    private static Box ReadBoxHeader(ReadOnlySpan<byte> data, long offset, long end)
    {
        if (end - offset < 8)
            throw new InvalidDataException("A box header is truncated.");

        var start = (int)offset;
        ulong size = BinaryPrimitives.ReadUInt32BigEndian(data[start..]);
        var type = Encoding.Latin1.GetString(data.Slice(start + 4, 4));
        var headerLength = 8;
        var isLarge = false;
        var extends = false;
        if (size == 1)
        {
            if (end - offset < 16)
                throw new InvalidDataException($"The large size of the box '{type}' is truncated.");

            size = BinaryPrimitives.ReadUInt64BigEndian(data[(start + 8)..]);
            headerLength = 16;
            isLarge = true;
        }
        else if (size == 0)
        {
            size = (ulong)(end - offset);
            extends = true;
        }

        if (type == "uuid")
            headerLength += 16;

        if (size < (ulong)headerLength || size > (ulong)(end - offset))
            throw new InvalidDataException($"The box '{type}' at {offset.ToString(CultureInfo.InvariantCulture)} declares {size.ToString(CultureInfo.InvariantCulture)} bytes, which do not fit its container.");

        return new Box(type, offset, headerLength, (long)size, isLarge, extends);
    }

    private static IEnumerable<(Box Box, ReadOnlyMemory<byte> Payload)> Children(ReadOnlyMemory<byte> container)
    {
        var offset = 0L;
        while (offset < container.Length)
        {
            var box = ReadBoxHeader(container.Span, offset, container.Length);
            yield return (box, container.Slice((int)(offset + box.HeaderLength), (int)(box.Size - box.HeaderLength)));
            offset += box.Size;
        }
    }

    private void ReadFileType(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 || payload.Length % 4 != 0)
            throw new InvalidDataException("The file type box is not a list of brands.");

        MajorBrand = Encoding.Latin1.GetString(payload[..4]);
        for (var i = 8; i < payload.Length; i += 4)
        {
            _compatibleBrands.Add(Encoding.Latin1.GetString(payload.Slice(i, 4)));
        }
    }

    // ISO/IEC 14496-12, section 8.11 "Meta Box" and ISO/IEC 23008-12, section 9.3 "Item Properties Box"
    private static MetaContent ReadMeta(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 4)
            throw new InvalidDataException("The 'meta' box has no version and flags.");

        var meta = new MetaContent();
        var associations = new List<(uint ItemId, int Index, bool Essential)>();
        foreach (var (box, content) in Children(payload[4..]))
        {
            var reader = new Cursor(content);
            switch (box.Type)
            {
                case "pitm":
                    meta.PrimaryItemId = reader.FullBoxVersion() == 0 ? reader.U16() : reader.U32();
                    break;
                case "iinf":
                    ReadItemInfos(content, meta);
                    break;
                case "iloc":
                    ReadItemLocations(content, meta);
                    break;
                case "iref":
                    ReadItemReferences(content, meta);
                    break;
                case "idat":
                    meta.ItemData = content;
                    break;
                case "iprp":
                    foreach (var (child, childContent) in Children(content))
                    {
                        if (child.Type == "ipco")
                        {
                            foreach (var (property, propertyContent) in Children(childContent))
                            {
                                meta.Properties.Add(new Property(meta.Properties.Count + 1, property.Type, propertyContent));
                            }
                        }
                        else if (child.Type == "ipma")
                        {
                            ReadAssociations(childContent, associations);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        foreach (var (itemId, index, essential) in associations)
        {
            if (index < 1 || index > meta.Properties.Count)
                throw new InvalidDataException("An item is associated with a property that does not exist.");

            meta.GetOrAdd(itemId).AddAssociation(new Association(meta.Properties[index - 1], essential));
        }

        meta.Items.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var item in meta.Items)
        {
            item.ItemDataBox = meta.ItemData;
        }

        return meta;
    }

    private static void ReadItemInfos(ReadOnlyMemory<byte> content, MetaContent meta)
    {
        var reader = new Cursor(content);
        var version = reader.FullBoxVersion();
        var count = version == 0 ? reader.U16() : reader.U32();
        var entries = Children(content[reader.Position..]).ToList();
        if (entries.Count != count)
            throw new InvalidDataException("The 'iinf' box does not have the number of entries it declares.");

        foreach (var (box, entry) in entries)
        {
            if (box.Type != "infe")
                continue;

            var info = new Cursor(entry);
            var word = info.U32();
            var entryVersion = (int)(word >> 24);
            if (entryVersion < 2)
                continue;

            var item = meta.GetOrAdd(entryVersion == 2 ? info.U16() : info.U32());
            item.IsHidden = (word & 1) != 0;
            item.ProtectionIndex = info.U16();
            item.Type = info.FourCC();
            item.Name = info.CString();
            if (item.Type == "mime")
                item.ContentType = info.CString();
        }
    }

    // ISO/IEC 14496-12, section 8.11.3 "The Item Location Box"
    private static void ReadItemLocations(ReadOnlyMemory<byte> content, MetaContent meta)
    {
        var reader = new Cursor(content);
        var version = reader.FullBoxVersion();
        var sizes = reader.U8();
        var (offsetSize, lengthSize) = (sizes >> 4, sizes & 15);
        sizes = reader.U8();
        var (baseOffsetSize, indexSize) = (sizes >> 4, version > 0 ? sizes & 15 : 0);
        var count = version < 2 ? reader.U16() : reader.U32();
        for (var i = 0u; i < count; i++)
        {
            var item = meta.GetOrAdd(version < 2 ? reader.U16() : reader.U32());
            item.ConstructionMethod = version > 0 ? reader.U16() & 15 : 0;
            item.DataReferenceIndex = reader.U16();
            var baseOffset = reader.Sized(baseOffsetSize);
            var extentCount = reader.U16();
            for (var j = 0; j < extentCount; j++)
            {
                reader.Sized(indexSize);
                var extentOffset = reader.Sized(offsetSize);
                var extentLength = reader.Sized(lengthSize);
                item.AddExtent(checked((long)(baseOffset + extentOffset)), checked((long)extentLength));
            }
        }
    }

    private static void ReadItemReferences(ReadOnlyMemory<byte> content, MetaContent meta)
    {
        var version = new Cursor(content).FullBoxVersion();
        foreach (var (box, reference) in Children(content[4..]))
        {
            var reader = new Cursor(reference);
            var from = version == 0 ? reader.U16() : reader.U32();
            var count = reader.U16();
            var to = new uint[count];
            for (var i = 0; i < count; i++)
            {
                to[i] = version == 0 ? reader.U16() : reader.U32();
            }

            meta.References.Add(new ItemReference(box.Type, from, to));
        }
    }

    private static void ReadAssociations(ReadOnlyMemory<byte> content, List<(uint ItemId, int Index, bool Essential)> associations)
    {
        var reader = new Cursor(content);
        var word = reader.U32();
        var version = (int)(word >> 24);
        var wide = (word & 1) != 0;
        var count = reader.U32();
        for (var i = 0u; i < count; i++)
        {
            var itemId = version < 1 ? reader.U16() : reader.U32();
            var associationCount = reader.U8();
            for (var j = 0; j < associationCount; j++)
            {
                var value = wide ? reader.U16() : reader.U8();
                var essential = (value & (wide ? 0x8000 : 0x80)) != 0;
                var index = value & (wide ? 0x7FFF : 0x7F);
                if (index != 0)
                    associations.Add((itemId, index, essential));
            }
        }
    }

    // ISO/IEC 14496-12, section 8.2 "Movie Structure" and 8.3 "Track Structure"
    private void ReadMovie(ReadOnlyMemory<byte> payload)
    {
        foreach (var (box, content) in Children(payload))
        {
            if (box.Type == "mvhd")
            {
                var reader = new Cursor(content);
                var version = reader.FullBoxVersion();
                reader.Skip(version == 1 ? 16 : 8);
                MovieTimescale = reader.U32();
            }
            else if (box.Type == "trak")
            {
                _tracks.Add(ReadTrack(content));
            }
        }
    }

    private static Track ReadTrack(ReadOnlyMemory<byte> payload)
    {
        var track = new Track();
        foreach (var (box, content) in Children(payload))
        {
            var reader = new Cursor(content);
            switch (box.Type)
            {
                case "tkhd":
                {
                    var version = reader.FullBoxVersion();
                    reader.Skip(version == 1 ? 16 : 8);
                    track.Id = reader.U32();
                    reader.Skip(4);
                    if (version == 1)
                    {
                        var duration = reader.U64();
                        track.Duration = duration == ulong.MaxValue ? null : duration;
                    }
                    else
                    {
                        var duration = reader.U32();
                        track.Duration = duration == uint.MaxValue ? null : duration;
                    }

                    break;
                }

                case "tref":
                    foreach (var (reference, ids) in Children(content))
                    {
                        var list = new uint[ids.Length / 4];
                        for (var i = 0; i < list.Length; i++)
                        {
                            list[i] = BinaryPrimitives.ReadUInt32BigEndian(ids.Span[(4 * i)..]);
                        }

                        track.AddReference(new TrackReference(reference.Type, list));
                    }

                    break;
                case "edts":
                    foreach (var (child, edits) in Children(content))
                    {
                        if (child.Type == "elst")
                            track.EditList = ReadEditList(edits);
                    }

                    break;
                case "meta" when !track.HasMeta:
                    track.SetMeta(ReadMeta(content).Items);
                    break;
                case "mdia":
                    ReadMedia(content, track);
                    break;
                default:
                    break;
            }
        }

        return track;
    }

    // ISO/IEC 14496-12, section 8.6.6 "Edit List Box"
    private static EditList ReadEditList(ReadOnlyMemory<byte> content)
    {
        var reader = new Cursor(content);
        var word = reader.U32();
        var version = (int)(word >> 24);
        var count = reader.U32();
        var entries = new List<Edit>();
        for (var i = 0u; i < count; i++)
        {
            ulong? segmentDuration;
            long mediaTime;
            if (version == 1)
            {
                var duration = reader.U64();
                segmentDuration = duration == ulong.MaxValue ? null : duration;
                mediaTime = (long)reader.U64();
            }
            else
            {
                var duration = reader.U32();
                segmentDuration = duration == uint.MaxValue ? null : duration;
                mediaTime = (int)reader.U32();
            }

            entries.Add(new Edit(segmentDuration, mediaTime, (short)reader.U16(), (short)reader.U16()));
        }

        return new EditList((word & 1) != 0, entries);
    }

    private static void ReadMedia(ReadOnlyMemory<byte> payload, Track track)
    {
        foreach (var (box, content) in Children(payload))
        {
            var reader = new Cursor(content);
            switch (box.Type)
            {
                case "mdhd":
                {
                    var version = reader.FullBoxVersion();
                    reader.Skip(version == 1 ? 16 : 8);
                    track.Timescale = reader.U32();
                    break;
                }

                case "hdlr":
                    reader.Skip(8);
                    track.HandlerType = reader.FourCC();
                    break;
                case "minf":
                    foreach (var (child, table) in Children(content))
                    {
                        if (child.Type == "stbl")
                            ReadSampleTable(table, track);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    // ISO/IEC 14496-12, section 8.5 "Sample Tables", 8.6.1.2 "Decoding Time to Sample Box" and 8.7 "Track Data Layout Structures"
    private static void ReadSampleTable(ReadOnlyMemory<byte> payload, Track track)
    {
        var durations = new List<uint>();
        var sizes = new List<int>();
        var runs = new List<(uint FirstChunk, uint SamplesPerChunk)>();
        var chunkOffsets = new List<long>();
        foreach (var (box, content) in Children(payload))
        {
            var reader = new Cursor(content);
            switch (box.Type)
            {
                case "stsd":
                    reader.Skip(4);
                    if (reader.U32() >= 1)
                    {
                        var (entry, entryContent) = Children(content[8..]).First();
                        track.SampleEntryType = entry.Type;
                        ReadVisualSampleEntry(entryContent, track);
                    }

                    break;
                case "stts":
                {
                    reader.Skip(4);
                    var count = reader.U32();
                    for (var i = 0u; i < count; i++)
                    {
                        var sampleCount = reader.U32();
                        var delta = reader.U32();
                        for (var j = 0u; j < sampleCount; j++)
                        {
                            durations.Add(delta);
                        }
                    }

                    break;
                }

                case "stsz":
                {
                    track.SampleSizeBox = "stsz";
                    reader.Skip(4);
                    var constant = reader.U32();
                    var count = reader.U32();
                    for (var i = 0u; i < count; i++)
                    {
                        sizes.Add(checked((int)(constant != 0 ? constant : reader.U32())));
                    }

                    break;
                }

                case "stss":
                {
                    // ISO/IEC 14496-12, section 8.6.2 "Sync Sample Box": the numbers of the sync samples, from 1
                    reader.Skip(4);
                    var count = reader.U32();
                    var syncSamples = new List<uint>();
                    for (var i = 0u; i < count; i++)
                    {
                        syncSamples.Add(reader.U32());
                    }

                    track.SyncSamples = syncSamples;
                    break;
                }

                case "stz2":
                {
                    track.SampleSizeBox = "stz2";
                    reader.Skip(7);
                    var fieldSize = reader.U8();
                    var count = reader.U32();
                    var pair = 0;
                    for (var i = 0u; i < count; i++)
                    {
                        if (fieldSize == 4)
                        {
                            if (i % 2 == 0)
                                pair = reader.U8();

                            sizes.Add(i % 2 == 0 ? pair >> 4 : pair & 15);
                        }
                        else
                        {
                            sizes.Add(fieldSize switch { 8 => reader.U8(), 16 => reader.U16(), _ => throw new InvalidDataException("The field size of 'stz2' is 4, 8 or 16.") });
                        }
                    }

                    break;
                }

                case "stsc":
                {
                    reader.Skip(4);
                    var count = reader.U32();
                    for (var i = 0u; i < count; i++)
                    {
                        runs.Add((reader.U32(), reader.U32()));
                        reader.Skip(4);
                    }

                    break;
                }

                case "stco" or "co64":
                {
                    track.ChunkOffsetBox = box.Type;
                    reader.Skip(4);
                    var count = reader.U32();
                    for (var i = 0u; i < count; i++)
                    {
                        chunkOffsets.Add(box.Type == "stco" ? reader.U32() : checked((long)reader.U64()));
                    }

                    break;
                }

                default:
                    break;
            }
        }

        // The samples of a chunk are contiguous; a run gives the number of samples of its chunks up to the next run
        var sample = 0;
        for (var chunk = 1; chunk <= chunkOffsets.Count && sample < sizes.Count; chunk++)
        {
            var samplesPerChunk = 0u;
            foreach (var run in runs)
            {
                if (run.FirstChunk <= chunk)
                    samplesPerChunk = run.SamplesPerChunk;
            }

            var offset = chunkOffsets[chunk - 1];
            for (var i = 0u; i < samplesPerChunk && sample < sizes.Count; i++)
            {
                if (sample >= durations.Count)
                    throw new InvalidDataException("The 'stts' box does not give the duration of every sample.");

                track.AddSample(new Sample(offset, sizes[sample], durations[sample]));
                offset += sizes[sample];
                sample++;
            }
        }

        if (sample != sizes.Count)
            throw new InvalidDataException("The chunks do not locate every sample.");
    }

    // ISO/IEC 14496-12, section 12.1.3 "Sample entry": 8 bytes of SampleEntry, 70 bytes of VisualSampleEntry, then boxes
    private static void ReadVisualSampleEntry(ReadOnlyMemory<byte> content, Track track)
    {
        if (content.Length < 78)
            throw new InvalidDataException("The visual sample entry is truncated.");

        track.Width = BinaryPrimitives.ReadUInt16BigEndian(content.Span[24..]);
        track.Height = BinaryPrimitives.ReadUInt16BigEndian(content.Span[26..]);
        foreach (var (box, child) in Children(content[78..]))
        {
            track.AddSampleEntryBox(new SampleEntryBox(box.Type, child));
            switch (box.Type)
            {
                case "av1C":
                    track.Configuration = Av1Configuration.Parse(child.Span);
                    track.ConfigurationRecord = child;
                    break;
                case "colr" when Encoding.Latin1.GetString(child.Span[..4]) == "nclx":
                    track.Nclx ??= ColorDescription.Parse(child.Span[4..]);
                    break;
                case "colr" when Encoding.Latin1.GetString(child.Span[..4]) is "prof" or "rICC":
                    track.IccProfile ??= child[4..];
                    break;
                case "auxi":
                    track.AuxiliaryType = new Cursor(child[4..]).CString();
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>A box: its type and its position in its container.</summary>
    /// <param name="Type">The four-character code.</param>
    /// <param name="Offset">The offset of the box header.</param>
    /// <param name="HeaderLength">The length of the header: 8, or 16 with a 64-bit size (16 more for a <c>uuid</c> box).</param>
    /// <param name="Size">The size of the box with its header.</param>
    /// <param name="HasLargeSize">Whether the size is stored in the 64-bit field.</param>
    /// <param name="ExtendsToEnd">Whether the size field is 0: the box extends to the end of its container.</param>
    public sealed record Box(string Type, long Offset, int HeaderLength, long Size, bool HasLargeSize, bool ExtendsToEnd);

    /// <summary>An item property of <c>ipco</c>.</summary>
    /// <param name="Index">The 1-based index of the property.</param>
    /// <param name="Type">The four-character code.</param>
    /// <param name="Payload">The payload of the property box (with the version and flags of a full box).</param>
    public sealed record Property(int Index, string Type, ReadOnlyMemory<byte> Payload);

    /// <summary>The content light level of a <c>clli</c> box (ISO/IEC 14496-12, section 12.1.6), in candelas per square meter.</summary>
    /// <param name="MaxContentLightLevel">The light level of the brightest pixel, or 0 when unknown.</param>
    /// <param name="MaxPictureAverageLightLevel">The largest average light level of a picture, or 0 when unknown.</param>
    public sealed record ContentLightLevel(int MaxContentLightLevel, int MaxPictureAverageLightLevel)
    {
        /// <summary>Parses the payload of the box: two 16-bit fields.</summary>
        /// <param name="payload">The payload.</param>
        /// <returns>The light levels.</returns>
        /// <exception cref="InvalidDataException">The box is truncated.</exception>
        public static ContentLightLevel Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
                throw new InvalidDataException("The 'clli' box is truncated.");

            return new ContentLightLevel(BinaryPrimitives.ReadUInt16BigEndian(payload), BinaryPrimitives.ReadUInt16BigEndian(payload[2..]));
        }
    }

    /// <summary>
    /// The mastering display color volume of a <c>mdcv</c> box (ISO/IEC 14496-12, section 12.1.7): chromaticities in units
    /// of 0.00002, luminances in units of 0.0001 candela per square meter. The box stores the three primaries with the
    /// semantics of the SEI message of Rec. ITU-T H.265 (section D.3.28): green, then blue, then red.
    /// </summary>
    /// <param name="Red">The x and y of the red primary.</param>
    /// <param name="Green">The x and y of the green primary.</param>
    /// <param name="Blue">The x and y of the blue primary.</param>
    /// <param name="WhitePoint">The x and y of the white point.</param>
    /// <param name="MaxLuminance">The nominal maximum luminance of the display.</param>
    /// <param name="MinLuminance">The nominal minimum luminance of the display.</param>
    public sealed record MasteringDisplay((int X, int Y) Red, (int X, int Y) Green, (int X, int Y) Blue, (int X, int Y) WhitePoint, long MaxLuminance, long MinLuminance)
    {
        /// <summary>Parses the payload of the box: eight 16-bit fields, then two 32-bit fields.</summary>
        /// <param name="payload">The payload.</param>
        /// <returns>The color volume.</returns>
        /// <exception cref="InvalidDataException">The box is truncated.</exception>
        public static MasteringDisplay Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 24)
                throw new InvalidDataException("The 'mdcv' box is truncated.");

            (int X, int Y) Pair(ReadOnlySpan<byte> data, int index) => (BinaryPrimitives.ReadUInt16BigEndian(data[(index * 4)..]), BinaryPrimitives.ReadUInt16BigEndian(data[((index * 4) + 2)..]));
            return new MasteringDisplay(Pair(payload, 2), Pair(payload, 0), Pair(payload, 1), Pair(payload, 3), BinaryPrimitives.ReadUInt32BigEndian(payload[16..]), BinaryPrimitives.ReadUInt32BigEndian(payload[20..]));
        }
    }

    /// <summary>The pixel aspect ratio of a <c>pasp</c> box (ISO/IEC 14496-12, section 12.1.4): the relative width and height of a pixel.</summary>
    /// <param name="HorizontalSpacing">The relative width of a pixel.</param>
    /// <param name="VerticalSpacing">The relative height of a pixel.</param>
    public sealed record PixelAspectRatio(long HorizontalSpacing, long VerticalSpacing)
    {
        /// <summary>Parses the payload of the box: two 32-bit fields.</summary>
        /// <param name="payload">The payload.</param>
        /// <returns>The ratio.</returns>
        /// <exception cref="InvalidDataException">The box is truncated.</exception>
        public static PixelAspectRatio Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
                throw new InvalidDataException("The 'pasp' box is truncated.");

            return new PixelAspectRatio(BinaryPrimitives.ReadUInt32BigEndian(payload), BinaryPrimitives.ReadUInt32BigEndian(payload[4..]));
        }
    }

    /// <summary>A box of a visual sample entry (<c>stsd</c>).</summary>
    /// <param name="Type">The four-character code.</param>
    /// <param name="Payload">The payload of the box (with the version and flags of a full box).</param>
    public sealed record SampleEntryBox(string Type, ReadOnlyMemory<byte> Payload);

    /// <summary>The association of a property with an item (<c>ipma</c>).</summary>
    /// <param name="Property">The property.</param>
    /// <param name="IsEssential">Whether a reader must understand the property to use the item.</param>
    public sealed record Association(Property Property, bool IsEssential);

    /// <summary>An item reference (<c>iref</c>).</summary>
    /// <param name="Type">The reference type: <c>dimg</c>, <c>auxl</c>, <c>cdsc</c>, <c>prem</c>, <c>thmb</c>...</param>
    /// <param name="FromItemId">The item that refers.</param>
    /// <param name="ToItemIds">The items referred to, in order.</param>
    public sealed record ItemReference(string Type, uint FromItemId, IReadOnlyList<uint> ToItemIds);

    /// <summary>A track reference (<c>tref</c>).</summary>
    /// <param name="Type">The reference type: <c>auxl</c>, <c>prem</c>...</param>
    /// <param name="TrackIds">The tracks referred to.</param>
    public sealed record TrackReference(string Type, IReadOnlyList<uint> TrackIds);

    /// <summary>A sample of a track.</summary>
    /// <param name="Offset">The offset of the sample in the file.</param>
    /// <param name="Size">The number of bytes of the sample.</param>
    /// <param name="Duration">The duration of the sample, in units of the time scale of the media.</param>
    public sealed record Sample(long Offset, int Size, uint Duration);

    /// <summary>An entry of an edit list.</summary>
    /// <param name="SegmentDuration">The duration of the edit in units of the time scale of the movie; <see langword="null"/> when every bit is set.</param>
    /// <param name="MediaTime">The starting time in the media, or -1 for an empty edit.</param>
    /// <param name="MediaRateInteger">The integer part of the rate.</param>
    /// <param name="MediaRateFraction">The fractional part of the rate.</param>
    public sealed record Edit(ulong? SegmentDuration, long MediaTime, short MediaRateInteger, short MediaRateFraction);

    /// <summary>An edit list (<c>elst</c>).</summary>
    /// <param name="IsRepeating">Whether the flag that repeats the edits is set (flags and 1).</param>
    /// <param name="Entries">The edits.</param>
    public sealed record EditList(bool IsRepeating, IReadOnlyList<Edit> Entries);

    /// <summary>The fields of an AV1 codec configuration record (AV1 Codec ISO Media File Format Binding, section 2.3).</summary>
    /// <param name="SequenceProfile">The <c>seq_profile</c> field.</param>
    /// <param name="SequenceLevelIndex">The <c>seq_level_idx_0</c> field.</param>
    /// <param name="BitDepth">8, 10 or 12, from <c>high_bitdepth</c> and <c>twelve_bit</c>.</param>
    /// <param name="IsMonochrome">The <c>monochrome</c> field.</param>
    /// <param name="SubsamplingX">The <c>chroma_subsampling_x</c> field.</param>
    /// <param name="SubsamplingY">The <c>chroma_subsampling_y</c> field.</param>
    /// <param name="ChromaSamplePosition">The <c>chroma_sample_position</c> field.</param>
    public sealed record Av1Configuration(int SequenceProfile, int SequenceLevelIndex, int BitDepth, bool IsMonochrome, int SubsamplingX, int SubsamplingY, int ChromaSamplePosition)
    {
        /// <summary>Parses the payload of an <c>av1C</c> box.</summary>
        /// <param name="payload">The payload.</param>
        /// <returns>The configuration.</returns>
        /// <exception cref="InvalidDataException">The record is truncated, or its marker and version are not 1 and 1.</exception>
        public static Av1Configuration Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4 || payload[0] != 0x81)
                throw new InvalidDataException("The AV1 configuration record is not valid.");

            var flags = payload[2];
            var highBitDepth = (flags & 0x40) != 0;
            var twelveBit = (flags & 0x20) != 0;
            return new Av1Configuration(payload[1] >> 5, payload[1] & 31, highBitDepth ? (twelveBit ? 12 : 10) : 8, (flags & 0x10) != 0, (flags >> 3) & 1, (flags >> 2) & 1, flags & 3);
        }
    }

    /// <summary>A <c>colr</c> box of type <c>nclx</c>: the code points of ITU-T H.273.</summary>
    /// <param name="ColorPrimaries">The color primaries.</param>
    /// <param name="TransferCharacteristics">The transfer characteristics.</param>
    /// <param name="MatrixCoefficients">The matrix coefficients.</param>
    /// <param name="FullRange">The full range flag.</param>
    public sealed record ColorDescription(int ColorPrimaries, int TransferCharacteristics, int MatrixCoefficients, bool FullRange)
    {
        /// <summary>Parses the fields that follow the color type of a <c>colr</c> box.</summary>
        /// <param name="payload">The fields.</param>
        /// <returns>The description.</returns>
        /// <exception cref="InvalidDataException">The box is truncated.</exception>
        public static ColorDescription Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 7)
                throw new InvalidDataException("The 'nclx' color information is truncated.");

            return new ColorDescription(BinaryPrimitives.ReadUInt16BigEndian(payload), BinaryPrimitives.ReadUInt16BigEndian(payload[2..]), BinaryPrimitives.ReadUInt16BigEndian(payload[4..]), (payload[6] & 0x80) != 0);
        }
    }

    /// <summary>
    /// A clean aperture (<c>clap</c>, ISO/IEC 14496-12, section 12.1.4): the size of the aperture and the offset of its center
    /// from the center of the image, as exact fractions.
    /// </summary>
    /// <param name="WidthNumerator">The numerator of the width.</param>
    /// <param name="WidthDenominator">The denominator of the width.</param>
    /// <param name="HeightNumerator">The numerator of the height.</param>
    /// <param name="HeightDenominator">The denominator of the height.</param>
    /// <param name="HorizontalOffsetNumerator">The numerator of the horizontal offset (signed).</param>
    /// <param name="HorizontalOffsetDenominator">The denominator of the horizontal offset.</param>
    /// <param name="VerticalOffsetNumerator">The numerator of the vertical offset (signed).</param>
    /// <param name="VerticalOffsetDenominator">The denominator of the vertical offset.</param>
    public sealed record CleanAperture(uint WidthNumerator, uint WidthDenominator, uint HeightNumerator, uint HeightDenominator, int HorizontalOffsetNumerator, uint HorizontalOffsetDenominator, int VerticalOffsetNumerator, uint VerticalOffsetDenominator)
    {
        /// <summary>Parses the payload of a <c>clap</c> box: four fractions of 32-bit terms.</summary>
        /// <param name="payload">The payload.</param>
        /// <returns>The aperture.</returns>
        /// <exception cref="InvalidDataException">The box is truncated.</exception>
        public static CleanAperture Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 32)
                throw new InvalidDataException("The 'clap' box is truncated.");

            return new CleanAperture(
                BinaryPrimitives.ReadUInt32BigEndian(payload), BinaryPrimitives.ReadUInt32BigEndian(payload[4..]),
                BinaryPrimitives.ReadUInt32BigEndian(payload[8..]), BinaryPrimitives.ReadUInt32BigEndian(payload[12..]),
                BinaryPrimitives.ReadInt32BigEndian(payload[16..]), BinaryPrimitives.ReadUInt32BigEndian(payload[20..]),
                BinaryPrimitives.ReadInt32BigEndian(payload[24..]), BinaryPrimitives.ReadUInt32BigEndian(payload[28..]));
        }

        /// <summary>
        /// Computes the rectangle of the aperture in an image: its center is at <c>(size - 1) / 2 + offset</c>, so its left
        /// edge is at <c>(size - apertureSize) / 2 + offset</c>.
        /// </summary>
        /// <param name="imageWidth">The width of the image.</param>
        /// <param name="imageHeight">The height of the image.</param>
        /// <returns>The rectangle, or <see langword="null"/> when its position or its size is not a whole number of samples.</returns>
        /// <exception cref="InvalidDataException">A denominator is zero.</exception>
        public (int X, int Y, int Width, int Height)? GetRectangle(int imageWidth, int imageHeight)
        {
            if (WidthDenominator == 0 || HeightDenominator == 0 || HorizontalOffsetDenominator == 0 || VerticalOffsetDenominator == 0)
                throw new InvalidDataException("A denominator of the clean aperture is zero.");

            if (WidthNumerator % WidthDenominator != 0 || HeightNumerator % HeightDenominator != 0)
                return null;

            var width = (long)(WidthNumerator / WidthDenominator);
            var height = (long)(HeightNumerator / HeightDenominator);

            // left = (imageWidth - width) / 2 + hN / hD = ((imageWidth - width) * hD + 2 hN) / (2 hD)
            var leftNumerator = ((BigInteger)(imageWidth - width) * HorizontalOffsetDenominator) + (2 * (BigInteger)HorizontalOffsetNumerator);
            var topNumerator = ((BigInteger)(imageHeight - height) * VerticalOffsetDenominator) + (2 * (BigInteger)VerticalOffsetNumerator);
            var leftDenominator = 2 * (BigInteger)HorizontalOffsetDenominator;
            var topDenominator = 2 * (BigInteger)VerticalOffsetDenominator;
            if (!(leftNumerator % leftDenominator).IsZero || !(topNumerator % topDenominator).IsZero)
                return null;

            return ((int)(leftNumerator / leftDenominator), (int)(topNumerator / topDenominator), (int)width, (int)height);
        }
    }

    /// <summary>An item of the <c>meta</c> box.</summary>
    public sealed class Item
    {
        private readonly List<(long Offset, long Length)> _extents = [];
        private readonly List<Association> _associations = [];

        internal Item(uint id)
        {
            Id = id;
        }

        /// <summary>Gets the item identifier.</summary>
        public uint Id { get; }

        /// <summary>Gets the item type (<c>infe</c>): <c>av01</c>, <c>grid</c>, <c>Exif</c>, <c>mime</c>...; empty without entry.</summary>
        public string Type { get; internal set; } = "";

        /// <summary>Gets the item name.</summary>
        public string Name { get; internal set; } = "";

        /// <summary>Gets the content type of a <c>mime</c> item, or <see langword="null"/>.</summary>
        public string? ContentType { get; internal set; }

        /// <summary>Gets a value indicating whether the item is hidden (flags and 1 of its <c>infe</c> entry).</summary>
        public bool IsHidden { get; internal set; }

        /// <summary>Gets the item protection index.</summary>
        public int ProtectionIndex { get; internal set; }

        /// <summary>Gets the construction method: 0 for file offsets, 1 for offsets in the <c>idat</c> box, 2 for item offsets.</summary>
        public int ConstructionMethod { get; internal set; }

        /// <summary>Gets the data reference index: 0 for this file.</summary>
        public int DataReferenceIndex { get; internal set; }

        /// <summary>Gets the extents of the item data, in order: the offset includes the base offset.</summary>
        public IReadOnlyList<(long Offset, long Length)> Extents => _extents;

        /// <summary>Gets the properties associated with the item, in association order.</summary>
        public IReadOnlyList<Association> Associations => _associations;

        /// <summary>Gets the payload of the <c>idat</c> box of the <c>meta</c> box the item belongs to (construction method 1).</summary>
        internal ReadOnlyMemory<byte> ItemDataBox { get; set; }

        /// <summary>Gets the size of the image (<c>ispe</c>), or <see langword="null"/>.</summary>
        public (long Width, long Height)? Size => Find("ispe") is { } property
            ? (BinaryPrimitives.ReadUInt32BigEndian(property.Payload.Span[4..]), BinaryPrimitives.ReadUInt32BigEndian(property.Payload.Span[8..]))
            : null;

        /// <summary>Gets the AV1 configuration (<c>av1C</c>), or <see langword="null"/>.</summary>
        public Av1Configuration? Configuration => Find("av1C") is { } property ? Av1Configuration.Parse(property.Payload.Span) : null;

        /// <summary>Gets the number of bits of each channel (<c>pixi</c>), or <see langword="null"/>.</summary>
        public IReadOnlyList<int>? PixelInformation => Find("pixi") is { } property ? [.. property.Payload.Span.Slice(5, property.Payload.Span[4]).ToArray().Select(value => (int)value)] : null;

        /// <summary>Gets the first <c>colr</c> property of type <c>nclx</c>, or <see langword="null"/>.</summary>
        public ColorDescription? Nclx => FindColor("nclx") is { } payload ? ColorDescription.Parse(payload.Span) : null;

        /// <summary>Gets the ICC profile of the first <c>colr</c> property of type <c>prof</c> or <c>rICC</c>, or <see langword="null"/>.</summary>
        public ReadOnlyMemory<byte>? IccProfile => FindColor("prof") ?? FindColor("rICC");

        /// <summary>Gets the first <c>clli</c> property, or <see langword="null"/>.</summary>
        public ContentLightLevel? ContentLightLevel => Find("clli") is { } property ? ReferenceAvif.ContentLightLevel.Parse(property.Payload.Span) : null;

        /// <summary>Gets the first <c>mdcv</c> property, or <see langword="null"/>.</summary>
        public MasteringDisplay? MasteringDisplay => Find("mdcv") is { } property ? ReferenceAvif.MasteringDisplay.Parse(property.Payload.Span) : null;

        /// <summary>Gets the first <c>pasp</c> property, or <see langword="null"/>.</summary>
        public PixelAspectRatio? PixelAspectRatio => Find("pasp") is { } property ? ReferenceAvif.PixelAspectRatio.Parse(property.Payload.Span) : null;

        /// <summary>Gets the auxiliary type (<c>auxC</c>), or <see langword="null"/>.</summary>
        public string? AuxiliaryType => Find("auxC") is { } property ? new Cursor(property.Payload[4..]).CString() : null;

        /// <summary>
        /// Gets the operating point of the AV1 bitstream that the item selects (<c>a1op</c>, AV1 Image File Format, section
        /// 2.3.2.1: one byte, <c>op_index</c>), or <see langword="null"/> without the property (operating point 0).
        /// </summary>
        public int? OperatingPoint => Find("a1op") is { } property ? property.Payload.Span[0] : null;

        /// <summary>
        /// Gets the layer that the item selects (<c>lsel</c>, ISO/IEC 23008-12, section 6.5.11: a 16-bit <c>layer_id</c>, which
        /// is an AV1 spatial layer; 0xFFFF selects no layer: every layer may be rendered and the last one is the image), or
        /// <see langword="null"/> without the property.
        /// </summary>
        public int? LayerSelector => Find("lsel") is { } property ? BinaryPrimitives.ReadUInt16BigEndian(property.Payload.Span) : null;

        /// <summary>
        /// Gets the sizes in bytes of the first three layers of the item data (<c>a1lx</c>, AV1 Image File Format, section
        /// 2.3.2.3: a byte whose low bit selects 32-bit sizes, then three sizes; 0 for a layer that does not exist or that is
        /// the last one, whose size is what remains), or <see langword="null"/> without the property.
        /// </summary>
        public IReadOnlyList<long>? LayerSizes
        {
            get
            {
                if (Find("a1lx") is not { } property)
                    return null;

                var span = property.Payload.Span;
                var large = (span[0] & 1) != 0;
                var sizes = new long[3];
                for (var i = 0; i < sizes.Length; i++)
                {
                    sizes[i] = large ? BinaryPrimitives.ReadUInt32BigEndian(span[(1 + (4 * i))..]) : BinaryPrimitives.ReadUInt16BigEndian(span[(1 + (2 * i))..]);
                }

                return sizes;
            }
        }

        /// <summary>Gets the transformative properties (<c>clap</c>, <c>irot</c>, <c>imir</c>), in association order.</summary>
        public IReadOnlyList<Property> Transforms => [.. _associations.Select(association => association.Property).Where(property => property.Type is "clap" or "irot" or "imir")];

        /// <summary>Gets the EXIF orientation that is equivalent to the <c>irot</c> and <c>imir</c> properties: 1 to 8.</summary>
        public int Orientation => ComposeOrientation(Transforms);

        /// <summary>Gets the clean aperture (<c>clap</c>), or <see langword="null"/>.</summary>
        public CleanAperture? CleanAperture => Find("clap") is { } property ? ReferenceAvif.CleanAperture.Parse(property.Payload.Span) : null;

        /// <summary>
        /// Gets a value indicating whether the transformative properties are associated in the order MIAF requires
        /// (ISO/IEC 23000-22, section 7.3.6.7): the clean aperture, the rotation, then the mirroring. The properties of a file
        /// that does otherwise still apply in the order of their association (ISO/IEC 23008-12, section 6.5.1).
        /// </summary>
        public bool HasMiafTransformOrder
        {
            get
            {
                var order = Transforms.Select(property => Array.IndexOf(["clap", "irot", "imir"], property.Type)).ToList();
                return order.SequenceEqual(order.Order());
            }
        }

        /// <summary>
        /// Computes the samples of the coded image that the transformative properties present, applied in the order of
        /// their association: a grid that holds the position of every coded sample is cropped, turned and mirrored as each
        /// property says, and what is left of it is a rectangle of the coded image.
        /// </summary>
        /// <param name="imageWidth">The width of the coded image.</param>
        /// <param name="imageHeight">The height of the coded image.</param>
        /// <returns>The rectangle of the coded image, or <see langword="null"/> when a clean aperture does not select whole samples.</returns>
        /// <exception cref="InvalidDataException">A clean aperture is not inside the image it applies to.</exception>
        public (int X, int Y, int Width, int Height)? GetPresentedRectangle(int imageWidth, int imageHeight)
        {
            if (imageWidth < 1 || imageHeight < 1 || (long)imageWidth * imageHeight > 1 << 24)
                throw new ArgumentOutOfRangeException(nameof(imageWidth), imageWidth, "The image is empty or too large for the grid of its samples.");

            var (width, height) = (imageWidth, imageHeight);
            var grid = new (int X, int Y)[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    grid[(y * width) + x] = (x, y);
                }
            }

            foreach (var property in Transforms)
            {
                var (sourceWidth, sourceHeight, source) = (width, height, grid);
                Func<int, int, (int X, int Y)> at;
                if (property.Type == "clap")
                {
                    if (ReferenceAvif.CleanAperture.Parse(property.Payload.Span).GetRectangle(width, height) is not { } aperture)
                        return null;

                    if (aperture.X < 0 || aperture.Y < 0 || aperture.Width < 1 || aperture.Height < 1 || aperture.X + aperture.Width > width || aperture.Y + aperture.Height > height)
                        throw new InvalidDataException("The clean aperture is not inside the image.");

                    (width, height) = (aperture.Width, aperture.Height);
                    at = (x, y) => source[((y + aperture.Y) * sourceWidth) + x + aperture.X];
                }
                else if (property.Type == "irot" && (property.Payload.Span[0] & 1) != 0)
                {
                    // A quarter turn anti-clockwise brings the right column to the top row; three of them bring the left
                    // column to the top row
                    (width, height) = (sourceHeight, sourceWidth);
                    at = (property.Payload.Span[0] & 3) == 1
                        ? (x, y) => source[(x * sourceWidth) + sourceWidth - 1 - y]
                        : (x, y) => source[((sourceHeight - 1 - x) * sourceWidth) + y];
                }
                else if (property.Type == "irot")
                {
                    at = (property.Payload.Span[0] & 3) == 0
                        ? (x, y) => source[(y * sourceWidth) + x]
                        : (x, y) => source[((sourceHeight - 1 - y) * sourceWidth) + sourceWidth - 1 - x];
                }
                else
                {
                    // imir: 0 exchanges the top and the bottom, 1 the left and the right
                    at = (property.Payload.Span[0] & 1) == 0
                        ? (x, y) => source[((sourceHeight - 1 - y) * sourceWidth) + x]
                        : (x, y) => source[(y * sourceWidth) + sourceWidth - 1 - x];
                }

                grid = new (int X, int Y)[width * height];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        grid[(y * width) + x] = at(x, y);
                    }
                }
            }

            var (left, top) = (grid.Min(position => position.X), grid.Min(position => position.Y));
            var (right, bottom) = (grid.Max(position => position.X), grid.Max(position => position.Y));
            if ((long)(right - left + 1) * (bottom - top + 1) != grid.Length || grid.Distinct().Count() != grid.Length)
                throw new InvalidDataException("The presented samples are not a rectangle of the coded image.");

            return (left, top, right - left + 1, bottom - top + 1);
        }

        /// <summary>Gets the first associated property of a type.</summary>
        /// <param name="type">The four-character code.</param>
        /// <returns>The property, or <see langword="null"/>.</returns>
        public Property? Find(string type) => _associations.FirstOrDefault(association => association.Property.Type == type)?.Property;

        internal void AddExtent(long offset, long length) => _extents.Add((offset, length));

        internal void AddAssociation(Association association) => _associations.Add(association);

        private ReadOnlyMemory<byte>? FindColor(string colorType)
        {
            foreach (var association in _associations)
            {
                var payload = association.Property.Payload;
                if (association.Property.Type == "colr" && payload.Length >= 4 && Encoding.Latin1.GetString(payload.Span[..4]) == colorType)
                    return payload[4..];
            }

            return null;
        }
    }

    /// <summary>A track of the <c>moov</c> box.</summary>
    public sealed class Track
    {
        private readonly List<TrackReference> _references = [];
        private readonly List<Sample> _samples = [];
        private readonly List<SampleEntryBox> _sampleEntryBoxes = [];
        private IReadOnlyList<Item> _metaItems = [];

        internal Track()
        {
        }

        /// <summary>Gets the track identifier (<c>tkhd</c>).</summary>
        public uint Id { get; internal set; }

        /// <summary>Gets the duration of the track in units of the time scale of the movie (<c>tkhd</c>); <see langword="null"/> when every bit is set (indefinite).</summary>
        public ulong? Duration { get; internal set; }

        /// <summary>Gets the handler type of the media: <c>pict</c> for an image sequence, <c>auxv</c> for an auxiliary one.</summary>
        public string HandlerType { get; internal set; } = "";

        /// <summary>Gets the number of time units of the media in a second (<c>mdhd</c>).</summary>
        public uint Timescale { get; internal set; }

        /// <summary>Gets the type of the first sample entry: <c>av01</c> for AV1.</summary>
        public string SampleEntryType { get; internal set; } = "";

        /// <summary>Gets the width of the visual sample entry.</summary>
        public int Width { get; internal set; }

        /// <summary>Gets the height of the visual sample entry.</summary>
        public int Height { get; internal set; }

        /// <summary>Gets the AV1 configuration of the sample entry, or <see langword="null"/>.</summary>
        public Av1Configuration? Configuration { get; internal set; }

        /// <summary>Gets the payload of the <c>av1C</c> box of the sample entry.</summary>
        public ReadOnlyMemory<byte> ConfigurationRecord { get; internal set; }

        /// <summary>Gets the <c>colr</c> box of type <c>nclx</c> of the sample entry, or <see langword="null"/>.</summary>
        public ColorDescription? Nclx { get; internal set; }

        /// <summary>Gets the ICC profile of the sample entry, or <see langword="null"/>.</summary>
        public ReadOnlyMemory<byte>? IccProfile { get; internal set; }

        /// <summary>Gets the auxiliary type of the sample entry (<c>auxi</c>), or <see langword="null"/>.</summary>
        public string? AuxiliaryType { get; internal set; }

        /// <summary>Gets the boxes of the visual sample entry, in file order: the configuration, the color information and whatever else describes the samples.</summary>
        public IReadOnlyList<SampleEntryBox> SampleEntryBoxes => _sampleEntryBoxes;

        /// <summary>Gets the first <c>clli</c> box of the sample entry, or <see langword="null"/>.</summary>
        public ContentLightLevel? ContentLightLevel => FindSampleEntryBox("clli") is { } box ? ReferenceAvif.ContentLightLevel.Parse(box.Payload.Span) : null;

        /// <summary>Gets the first <c>mdcv</c> box of the sample entry, or <see langword="null"/>.</summary>
        public MasteringDisplay? MasteringDisplay => FindSampleEntryBox("mdcv") is { } box ? ReferenceAvif.MasteringDisplay.Parse(box.Payload.Span) : null;

        /// <summary>Gets the first <c>pasp</c> box of the sample entry, or <see langword="null"/>.</summary>
        public PixelAspectRatio? PixelAspectRatio => FindSampleEntryBox("pasp") is { } box ? ReferenceAvif.PixelAspectRatio.Parse(box.Payload.Span) : null;

        /// <summary>Gets the edit list, or <see langword="null"/>.</summary>
        public EditList? EditList { get; internal set; }

        /// <summary>Gets the type of the box that stores the sample sizes: <c>stsz</c> or <c>stz2</c>.</summary>
        public string SampleSizeBox { get; internal set; } = "";

        /// <summary>Gets the type of the box that stores the chunk offsets: <c>stco</c> or <c>co64</c>.</summary>
        public string ChunkOffsetBox { get; internal set; } = "";

        /// <summary>Gets the track references.</summary>
        public IReadOnlyList<TrackReference> References => _references;

        /// <summary>Gets the samples, in decoding order.</summary>
        public IReadOnlyList<Sample> Samples => _samples;

        /// <summary>
        /// Gets the numbers of the sync samples (<c>stss</c>), from 1, or <see langword="null"/> when the track has no such box:
        /// every sample is then a sync sample.
        /// </summary>
        public IReadOnlyList<uint>? SyncSamples { get; internal set; }

        /// <summary>Gets a value indicating whether the track has a <c>meta</c> box (ISO/IEC 14496-12, section 8.11.1).</summary>
        public bool HasMeta { get; private set; }

        /// <summary>Gets the items of the <c>meta</c> box of the track, ordered by identifier: the items that describe the track.</summary>
        public IReadOnlyList<Item> MetaItems => _metaItems;

        /// <summary>
        /// Gets the number of times the sequence is played (AV1 Image File Format, section 3.2 "Image sequences"), or
        /// <see langword="null"/> when it repeats forever: a track without edit list repeats forever; an edit list that does
        /// not repeat is played once; a repeating edit list is played until the duration of the track, which is indefinite
        /// when every bit of the field is set; otherwise the number of plays is the duration of the track divided by the
        /// duration of the edit, rounded up. A number of plays above <see cref="int.MaxValue"/> stands for forever.
        /// </summary>
        public int? TotalPlays
        {
            get
            {
                if (EditList is not { } edits)
                    return null;

                if (!edits.IsRepeating)
                    return 1;

                if (Duration is not { } duration || edits.Entries.Count == 0 || edits.Entries[0].SegmentDuration is not { } segment || segment == 0)
                    return null;

                var plays = (new BigInteger(duration) + segment - 1) / segment;
                return plays > int.MaxValue ? null : (int)BigInteger.Max(plays, 1);
            }
        }

        /// <summary>Returns the first box of the visual sample entry of a type.</summary>
        /// <param name="type">The four-character code.</param>
        /// <returns>The box, or <see langword="null"/>.</returns>
        public SampleEntryBox? FindSampleEntryBox(string type) => _sampleEntryBoxes.FirstOrDefault(box => box.Type == type);

        internal void AddSampleEntryBox(SampleEntryBox box) => _sampleEntryBoxes.Add(box);

        internal void AddReference(TrackReference reference) => _references.Add(reference);

        internal void SetMeta(IReadOnlyList<Item> items)
        {
            HasMeta = true;
            _metaItems = items;
        }

        internal void AddSample(Sample sample) => _samples.Add(sample);
    }

    /// <summary>The content of a <c>meta</c> box, of the file or of a track.</summary>
    private sealed class MetaContent
    {
        public List<Item> Items { get; } = [];

        public List<ItemReference> References { get; } = [];

        public List<Property> Properties { get; } = [];

        public ReadOnlyMemory<byte> ItemData { get; set; }

        public uint? PrimaryItemId { get; set; }

        public Item? Find(uint id) => Items.FirstOrDefault(item => item.Id == id);

        public Item GetOrAdd(uint id)
        {
            var item = Find(id);
            if (item is null)
            {
                item = new Item(id);
                Items.Add(item);
            }

            return item;
        }
    }

    private struct Cursor(ReadOnlyMemory<byte> data)
    {
        public int Position { get; private set; }

        public byte U8() => Take(1)[0];

        public ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

        public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));

        public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));

        public int FullBoxVersion() => (int)(U32() >> 24);

        public string FourCC() => Encoding.Latin1.GetString(Take(4));

        public void Skip(int count) => Take(count);

        public ulong Sized(int size) => size switch
        {
            0 => 0,
            4 => U32(),
            8 => U64(),
            _ => throw new InvalidDataException("A field size of the 'iloc' box is 0, 4 or 8."),
        };

        public string CString()
        {
            var remaining = data.Span[Position..];
            var length = remaining.IndexOf((byte)0);
            if (length < 0)
                throw new InvalidDataException("A string is not terminated.");

            Position += length + 1;
            return Encoding.UTF8.GetString(remaining[..length]);
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > data.Length - Position)
                throw new InvalidDataException("A box is too short for its fields.");

            var result = data.Span.Slice(Position, count);
            Position += count;
            return result;
        }
    }
}
