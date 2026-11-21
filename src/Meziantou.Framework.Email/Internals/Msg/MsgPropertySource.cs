using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Email.Internals.Cfb;
using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Internals.Msg;

/// <summary>The properties of an object of an .msg file: the entries of its property stream, and the streams of the variable-size values ([MS-OXMSG] 2.4).</summary>
internal sealed class MsgPropertySource : PropertySource
{
    private const ushort InternetCodepage = 0x3FDE;

    private readonly MsgContext _context;
    private readonly CfbEntry _storage;

    private MsgPropertySource(MsgContext context, CfbEntry storage, Encoding defaultEncoding)
    {
        _context = context;
        _storage = storage;
        DefaultEncoding = defaultEncoding;
    }

    public override Encoding DefaultEncoding { get; }

    /// <summary>Reads the properties of a storage.</summary>
    /// <param name="context">The context of the file.</param>
    /// <param name="storage">The storage of the message, recipient or attachment.</param>
    /// <param name="headerSize">The size of the header of the property stream: 32 for the message of the file, 24 for an embedded message, 8 for a recipient or an attachment.</param>
    /// <param name="defaultEncoding">The encoding of the 8-bit strings when the object does not give its code page: the encoding of the message of a recipient or an attachment.</param>
    public static MapiPropertyCollection Read(MsgContext context, CfbEntry storage, int headerSize, Encoding defaultEncoding)
    {
        var properties = new List<RawProperty>();
        var tags = new HashSet<MapiPropertyTag>();
        var stream = storage.GetChild(MsgNames.Properties)?.ReadStream() ?? [];
        for (var offset = headerSize; offset + 16 <= stream.Length; offset += 16)
        {
            var tag = MapiPropertyTag.FromValue(BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset)));
            if (!tags.Add(tag))
            {
                continue;
            }

            var size = IsInline(tag.Type) ? PropertyTypes.GetFixedSize(tag.Type) : 0;
            properties.Add(size > 0 ? RawProperty.FromInline(tag, stream.AsSpan(offset + 8, size).ToArray()) : RawProperty.FromHnid(tag, 0));
        }

        // Some writers omit the entries of the values they store in streams
        foreach (var child in storage.Children.Values)
        {
            if (child.IsStream && MsgNames.TryParseStream(child.Name, out var tag) && !IsInline(tag.Type) && !PropertyTypes.IsMultiValued(tag.Type) && tags.Add(tag))
            {
                properties.Add(RawProperty.FromHnid(tag, 0));
            }
        }

        var embedded = new MapiPropertyTag(MsgNames.AttachDataObjectId, MapiPropertyType.ObjectReference);
        if (storage.GetChild(MsgNames.EmbeddedMessage) is { IsStorage: true } && tags.Add(embedded))
        {
            properties.Add(RawProperty.FromHnid(embedded, 0));
        }

        // A message without PidTagMessageCodepage gives the code page of its strings in PidTagInternetCodepage
        if (properties.Find(property => property.Tag == new MapiPropertyTag(InternetCodepage, MapiPropertyType.Integer32)).Inline is { Length: 4 } codePageBytes
            && BinaryPrimitives.ReadInt32LittleEndian(codePageBytes) is var codePage and > 0 and < 65536 and not 1200 and not 1201)
        {
            defaultEncoding = PstContext.GetEncoding(codePage);
        }

        return new MapiPropertyCollection(new MsgPropertySource(context, storage, defaultEncoding), properties);
    }

    /// <summary>Gets whether the value is in the entry of the property stream: the fixed-size types of 8 bytes or less.</summary>
    public static bool IsInline(MapiPropertyType type) => !PropertyTypes.IsMultiValued(type) && PropertyTypes.GetFixedSize(type) is > 0 and <= 8;

    public override MapiNamedProperty? GetName(ushort id) => _context.NameToIdMap.GetName(id);

    public override ushort? GetId(MapiNamedProperty name) => _context.NameToIdMap.GetId(name);

    public override byte[] ReadBytes(RawProperty property)
    {
        if (property.Inline is not null)
        {
            return property.Inline;
        }

        var tag = property.Tag;
        if (tag.Type == MapiPropertyType.ObjectReference)
        {
            return [];
        }

        if (tag.Type is MapiPropertyType.MultipleUnicode or MapiPropertyType.MultipleString8 or MapiPropertyType.MultipleBinary)
        {
            return ReadMultipleVariable(tag);
        }

        return PropertyValueCodec.RemoveTerminator(tag.Type, _storage.GetChild(MsgNames.Stream(tag))?.ReadStream() ?? []);
    }

    public override long GetSize(RawProperty property)
    {
        if (property.Inline is not null)
        {
            return property.Inline.Length;
        }

        if (property.Tag.Type is MapiPropertyType.MultipleUnicode or MapiPropertyType.MultipleString8 or MapiPropertyType.MultipleBinary or MapiPropertyType.Unicode or MapiPropertyType.String8)
        {
            return ReadBytes(property).Length;
        }

        return _storage.GetChild(MsgNames.Stream(property.Tag)) is { IsStream: true } stream ? stream.Size : 0;
    }

    /// <summary>Reads the values of a multi-valued property of variable-size values, one stream per value, in the format of an Outlook data file: a count, the offsets, then the values.</summary>
    private byte[] ReadMultipleVariable(MapiPropertyTag tag)
    {
        var lengths = _storage.GetChild(MsgNames.Stream(tag))?.ReadStream() ?? [];
        var count = lengths.Length / (tag.Type == MapiPropertyType.MultipleBinary ? 8 : 4);
        var values = new List<byte[]>(count);
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            var value = _storage.GetChild(MsgNames.Stream(tag, i))?.ReadStream() ?? [];
            total += value.Length;
            if (total > _context.Options.MaxPropertySize)
            {
                throw new EmailResourceLimitException($"The property {tag} is larger than the limit of {_context.Options.MaxPropertySize} bytes.");
            }

            values.Add(PropertyValueCodec.RemoveTerminator(PropertyTypes.GetElementType(tag.Type), value));
        }

        return PropertyValueCodec.JoinVariable(values);
    }
}
