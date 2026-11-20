using System.Buffers.Binary;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>The mapping between the identifiers of named properties (0x8000 and above) and their names ([MS-PST] 2.4.7).</summary>
internal sealed class NameToIdMap
{
    public const ushort BucketCountId = 0x0001;
    public const ushort GuidStreamId = 0x0002;
    public const ushort EntryStreamId = 0x0003;
    public const ushort StringStreamId = 0x0004;

    private readonly Dictionary<ushort, PstNamedProperty> _byId = [];
    private readonly Dictionary<PstNamedProperty, ushort> _byName = [];

    public static NameToIdMap Empty { get; } = new();

    private NameToIdMap()
    {
    }

    public static NameToIdMap Read(NdbReader reader)
    {
        var node = reader.GetNode(Nids.NameToIdMap);
        if (node is null)
        {
            return Empty;
        }

        var pc = new PropertyContext(node);
        byte[]? guids = null;
        byte[]? entries = null;
        byte[]? strings = null;
        foreach (var property in pc.Properties)
        {
            if (property.Tag.Type != PstPropertyType.Binary || property.Inline is not null)
            {
                continue;
            }

            switch (property.Tag.Id)
            {
                case GuidStreamId:
                    guids = ReadHnid(pc, node, property.Hnid, reader.Options.MaxPropertySize);
                    break;
                case EntryStreamId:
                    entries = ReadHnid(pc, node, property.Hnid, reader.Options.MaxPropertySize);
                    break;
                case StringStreamId:
                    strings = ReadHnid(pc, node, property.Hnid, reader.Options.MaxPropertySize);
                    break;
            }
        }

        var map = new NameToIdMap();
        if (entries is null)
        {
            return map;
        }

        guids ??= [];
        strings ??= [];
        for (var offset = 0; offset + 8 <= entries.Length; offset += 8)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(offset));
            var guidAndKind = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(offset + 4));
            var index = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(offset + 6));
            var isString = (guidAndKind & 1) != 0;
            var guidIndex = guidAndKind >> 1;
            Guid propertySet;
            if (guidIndex == 0)
            {
                // NAMEID_GUID_NONE: a name without a property set
                propertySet = Guid.Empty;
            }
            else if (guidIndex == 1)
            {
                propertySet = PstPropertySets.Mapi;
            }
            else if (guidIndex == 2)
            {
                propertySet = PstPropertySets.PublicStrings;
            }
            else if (guidIndex >= 3 && ((guidIndex - 3) * 16) + 16 <= guids.Length)
            {
                propertySet = new Guid(guids.AsSpan((guidIndex - 3) * 16, 16));
            }
            else
            {
                continue;
            }

            PstNamedProperty name;
            if (isString)
            {
                if (value > strings.Length - 4)
                {
                    continue;
                }

                var length = BinaryPrimitives.ReadUInt32LittleEndian(strings.AsSpan((int)value));
                if (length > strings.Length - value - 4)
                {
                    continue;
                }

                name = new PstNamedProperty(propertySet, PropertyValueCodec.DecodeUnicode(strings.AsSpan((int)value + 4, (int)length)));
            }
            else
            {
                name = new PstNamedProperty(propertySet, (int)value);
            }

            var id = (ushort)(0x8000 + index);
            if (index <= 0x7FFE && map._byId.TryAdd(id, name))
            {
                map._byName.TryAdd(name, id);
            }
        }

        return map;
    }

    public PstNamedProperty? GetName(ushort id) => _byId.TryGetValue(id, out var name) ? name : null;

    public ushort? GetId(PstNamedProperty name) => _byName.TryGetValue(name, out var id) ? id : null;

    private static byte[] ReadHnid(PropertyContext pc, NdbNode node, uint hnid, long maxSize)
    {
        if (hnid == 0)
        {
            return [];
        }

        if (HeapOnNode.IsHid(hnid))
        {
            return pc.Heap.GetItem(hnid).ToArray();
        }

        var subnode = node.GetSubnode(hnid) ?? throw new InvalidPstException($"The subnode 0x{hnid:X} of the name-to-ID map does not exist.");
        return subnode.ReadData(maxSize);
    }
}
