namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The content of the <c>meta</c> box of an AVIF file (ISO/IEC 14496-12, section 8.11, and ISO/IEC 23008-12): the items, their
/// references and the item properties. The box is parsed from memory; the properties and the <c>idat</c> box are kept as
/// ranges of the box payload.
/// </summary>
internal sealed class AvifMetaBox
{
    private readonly Dictionary<uint, AvifItem> _items = [];
    private readonly List<AvifItemReference> _references = [];
    private readonly List<(uint Type, int Offset, int Length)> _properties = [];

    private AvifMetaBox()
    {
    }

    /// <summary>Gets the identifier of the primary item, or <see langword="null"/> without <c>pitm</c> box.</summary>
    public uint? PrimaryItemId { get; private set; }

    public IReadOnlyDictionary<uint, AvifItem> Items => _items;

    public IReadOnlyList<AvifItemReference> References => _references;

    /// <summary>Gets the properties of <c>ipco</c>, in order: the type and the range of the payload of each one in the <c>meta</c> payload.</summary>
    public IReadOnlyList<(uint Type, int Offset, int Length)> Properties => _properties;

    /// <summary>Gets the range of the payload of the <c>idat</c> box in the <c>meta</c> payload, or an empty range.</summary>
    public (int Offset, int Length) ItemData { get; private set; }

    /// <summary>Parses the payload of a <c>meta</c> box.</summary>
    /// <exception cref="InvalidImageContentException">The box is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The box uses a version or a feature that is not supported.</exception>
    public static AvifMetaBox Parse(ReadOnlySpan<byte> payload)
    {
        var meta = new AvifMetaBox();
        var reader = new AvifBoxReader(payload);
        var (version, _) = reader.ReadFullBoxHeader();
        if (version is not 0)
            throw AvifFormat.Unsupported("The version of the AVIF 'meta' box is not supported.", "meta box version");

        var seen = new HashSet<uint>();
        while (reader.TryReadBox(out var type, out _, out var box))
        {
            var start = reader.Position - box.Remaining;
            switch (type)
            {
                case AvifFormat.Hdlr:
                    EnsureSingle(seen, type);
                    box.ReadFullBoxHeader();
                    box.ReadUInt32();
                    if (box.ReadUInt32() is not AvifFormat.Pict)
                        throw AvifFormat.Invalid("the handler of the 'meta' box is not 'pict'.");

                    break;
                case AvifFormat.Pitm:
                    EnsureSingle(seen, type);
                    meta.PrimaryItemId = box.ReadFullBoxHeader().Version is 0 ? box.ReadUInt16() : box.ReadUInt32();
                    break;
                case AvifFormat.Iloc:
                    EnsureSingle(seen, type);
                    meta.ReadItemLocations(ref box);
                    break;
                case AvifFormat.Iinf:
                    EnsureSingle(seen, type);
                    meta.ReadItemInfos(ref box);
                    break;
                case AvifFormat.Iref:
                    EnsureSingle(seen, type);
                    meta.ReadItemReferences(ref box);
                    break;
                case AvifFormat.Iprp:
                    EnsureSingle(seen, type);
                    meta.ReadItemProperties(ref box, start);
                    break;
                case AvifFormat.Idat:
                    EnsureSingle(seen, type);
                    meta.ItemData = (start, box.Remaining);
                    break;
                default:
                    // Groups, data references, protection and unknown boxes do not take part in the decoding of the primary item
                    break;
            }
        }

        return meta;
    }

    private static void EnsureSingle(HashSet<uint> seen, uint type)
    {
        if (!seen.Add(type))
            throw AvifFormat.Invalid($"the 'meta' box has two '{AvifFormat.ToText(type)}' boxes.");
    }

    private AvifItem GetOrAddItem(uint id)
    {
        if (!_items.TryGetValue(id, out var item))
        {
            item = new AvifItem(id);
            _items.Add(id, item);
        }

        return item;
    }

    // ISO/IEC 14496-12, section 8.11.3 "The Item Location Box"
    private void ReadItemLocations(ref AvifBoxReader box)
    {
        var (version, _) = box.ReadFullBoxHeader();
        if (version > 2)
            throw AvifFormat.Unsupported("The version of the AVIF 'iloc' box is not supported.", "iloc box version");

        var sizes = box.ReadByte();
        var offsetSize = sizes >> 4;
        var lengthSize = sizes & 0xF;
        sizes = box.ReadByte();
        var baseOffsetSize = sizes >> 4;
        var indexSize = version > 0 ? sizes & 0xF : 0;
        if (offsetSize is not (0 or 4 or 8) || lengthSize is not (0 or 4 or 8) || baseOffsetSize is not (0 or 4 or 8) || indexSize is not (0 or 4 or 8))
            throw AvifFormat.Invalid("the 'iloc' box has a field size that is not allowed.");

        var itemCount = version < 2 ? box.ReadUInt16() : box.ReadUInt32();
        for (uint i = 0; i < itemCount; i++)
        {
            var id = version < 2 ? box.ReadUInt16() : box.ReadUInt32();
            var item = GetOrAddItem(id);
            if (item.HasLocation)
                throw AvifFormat.Invalid("an item has two locations.");

            item.HasLocation = true;
            if (version > 0)
                item.ConstructionMethod = box.ReadUInt16() & 0xF;

            var dataReferenceIndex = box.ReadUInt16();
            if (dataReferenceIndex is not 0)
                throw AvifFormat.Unsupported("The data of an AVIF item is stored in another file.", "external item data");

            var baseOffset = box.ReadUInt(baseOffsetSize);
            var extentCount = box.ReadUInt16();
            for (var j = 0; j < extentCount; j++)
            {
                if (indexSize > 0)
                    box.ReadUInt(indexSize);

                var extentOffset = box.ReadUInt(offsetSize);
                var extentLength = box.ReadUInt(lengthSize);
                if (baseOffset > long.MaxValue || extentOffset > (ulong)long.MaxValue - baseOffset || extentLength > long.MaxValue)
                    throw AvifFormat.Invalid("the location of an item is out of range.");

                item.Extents.Add(((long)(baseOffset + extentOffset), (long)extentLength));
            }
        }
    }

    // ISO/IEC 14496-12, section 8.11.6 "Item Information Box"
    private void ReadItemInfos(ref AvifBoxReader box)
    {
        var (version, _) = box.ReadFullBoxHeader();
        var entryCount = version is 0 ? box.ReadUInt16() : box.ReadUInt32();
        var typed = new HashSet<uint>();
        for (uint i = 0; i < entryCount; i++)
        {
            if (!box.TryReadBox(out var type, out _, out var entry))
                throw AvifFormat.Invalid("the 'iinf' box has fewer entries than it declares.");

            if (type is not AvifFormat.Infe)
                continue;

            var (entryVersion, flags) = entry.ReadFullBoxHeader();
            if (entryVersion is not (2 or 3))
                continue;

            var id = entryVersion is 2 ? entry.ReadUInt16() : entry.ReadUInt32();
            var protectionIndex = entry.ReadUInt16();
            var item = GetOrAddItem(id);
            if (!typed.Add(id))
                throw AvifFormat.Invalid("an item has two 'infe' entries.");

            item.Type = entry.ReadUInt32();
            item.IsHidden = (flags & 1) is not 0;
            if (protectionIndex is not 0)
                throw AvifFormat.Unsupported("An AVIF item is protected.", "protected items");

            entry.ReadCString();
            if (item.Type is AvifFormat.Mime)
                item.IsXmp = entry.ReadCString().SequenceEqual(AvifFormat.XmpContentType);
        }
    }

    // ISO/IEC 14496-12, section 8.11.12 "Item Reference Box"
    private void ReadItemReferences(ref AvifBoxReader box)
    {
        var (version, _) = box.ReadFullBoxHeader();
        if (version > 1)
            throw AvifFormat.Unsupported("The version of the AVIF 'iref' box is not supported.", "iref box version");

        while (box.TryReadBox(out var type, out _, out var reference))
        {
            var from = version is 0 ? reference.ReadUInt16() : reference.ReadUInt32();
            var count = reference.ReadUInt16();
            var to = new List<uint>();
            for (var i = 0; i < count; i++)
            {
                to.Add(version is 0 ? reference.ReadUInt16() : reference.ReadUInt32());
            }

            _references.Add(new AvifItemReference(type, from, [.. to]));
        }
    }

    // ISO/IEC 23008-12, section 9.3 "Item Properties Box"
    private void ReadItemProperties(ref AvifBoxReader box, int boxStart)
    {
        var sawContainer = false;
        while (box.TryReadBox(out var type, out var offset, out var child))
        {
            if (type is AvifFormat.Ipco)
            {
                if (sawContainer)
                    throw AvifFormat.Invalid("the 'iprp' box has two 'ipco' boxes.");

                sawContainer = true;
                var containerStart = boxStart + offset;
                while (child.TryReadBox(out var propertyType, out var propertyOffset, out var property))
                {
                    _properties.Add((propertyType, containerStart + propertyOffset, property.Remaining));
                }
            }
            else if (type is AvifFormat.Ipma)
            {
                var (version, flags) = child.ReadFullBoxHeader();
                var entryCount = child.ReadUInt32();
                for (uint i = 0; i < entryCount; i++)
                {
                    var id = version < 1 ? child.ReadUInt16() : child.ReadUInt32();
                    var item = GetOrAddItem(id);
                    if (item.Properties.Count > 0)
                        throw AvifFormat.Invalid("an item has two property associations.");

                    var associationCount = child.ReadByte();
                    for (var j = 0; j < associationCount; j++)
                    {
                        int value;
                        bool essential;
                        if ((flags & 1) is not 0)
                        {
                            value = child.ReadUInt16();
                            essential = (value & 0x8000) is not 0;
                            value &= 0x7FFF;
                        }
                        else
                        {
                            value = child.ReadByte();
                            essential = (value & 0x80) is not 0;
                            value &= 0x7F;
                        }

                        // The index 0 means that no property is associated
                        if (value is not 0)
                            item.Properties.Add((value, essential));
                    }
                }
            }
        }

        foreach (var item in _items.Values)
        {
            foreach (var (index, _) in item.Properties)
            {
                if (index > _properties.Count)
                    throw AvifFormat.Invalid("an item is associated with a property that does not exist.");
            }
        }
    }
}
