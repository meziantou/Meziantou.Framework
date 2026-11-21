using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail.Internals.Tnef;

/// <summary>Reads a TNEF stream ([MS-OXTNEF]): the attributes of the message and of its attachments, and the MAPI properties they contain.</summary>
internal static class TnefParser
{
    public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == TnefAttributes.Signature;

    public static TnefItemData Parse(ReadOnlySpan<byte> data, TnefContext context)
    {
        if (!HasSignature(data))
        {
            throw new InvalidMailDataException("The content is not a TNEF stream: the signature is missing.");
        }

        if (data.Length < 6)
        {
            throw new InvalidMailDataException("The TNEF stream is truncated.");
        }

        var item = new TnefItemData();
        var legacy = new List<TnefProperty>();
        var attachmentLegacy = new List<List<TnefProperty>>();
        var position = 6; // The signature, then the legacy key
        while (position < data.Length)
        {
            if (data.Length - position < 9)
            {
                // Some writers end the stream with a line break: the bytes that cannot be an attribute are ignored
                break;
            }

            var level = data[position];
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 1)..]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 5)..]);
            position += 9;
            if (length > (uint)(data.Length - position - 2) || data.Length - position < 2)
            {
                throw new InvalidMailDataException($"The attribute 0x{id:X8} of the TNEF stream is longer than the stream.");
            }

            var value = data.Slice(position, (int)length);
            var checksum = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + (int)length)..]);
            position += (int)length + 2;
            if (context.Options.VerifyChecksums && checksum != Checksum(value))
            {
                throw new InvalidMailDataException($"The checksum of the attribute 0x{id:X8} of the TNEF stream is invalid.");
            }

            if (level == TnefAttributes.AttachmentLevel)
            {
                if (id == TnefAttributes.AttachRenderData || item.Attachments.Count == 0)
                {
                    item.Attachments.Add([]);
                    attachmentLegacy.Add([]);
                }

                ReadAttachmentAttribute(id, value, item.Attachments[^1], attachmentLegacy[^1], context);
            }
            else
            {
                ReadMessageAttribute(id, value, item, legacy, context);
            }
        }

        // The MAPI properties win over the attributes that encode the same values
        AddMissing(item.Properties, legacy);
        for (var i = 0; i < item.Attachments.Count; i++)
        {
            AddMissing(item.Attachments[i], attachmentLegacy[i]);
        }

        return item;
    }

    public static ushort Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        foreach (var b in data)
        {
            sum += b;
        }

        return (ushort)sum;
    }

    private static void ReadMessageAttribute(uint id, ReadOnlySpan<byte> value, TnefItemData item, List<TnefProperty> legacy, TnefContext context)
    {
        switch (id)
        {
            case TnefAttributes.OemCodepage when value.Length >= 4:
                var codePage = BinaryPrimitives.ReadInt32LittleEndian(value);
                if (codePage is > 0 and < 65536)
                {
                    item.CodePage = codePage;
                }

                break;
            case TnefAttributes.MessageProperties:
                item.Properties.AddRange(ReadPropertyList(value, context));
                break;
            case TnefAttributes.RecipientTable:
                ReadRecipientTable(value, item, context);
                break;
            case TnefAttributes.Subject:
                legacy.Add(String8(PropertyIds.Subject, value));
                break;
            case TnefAttributes.Body:
                legacy.Add(String8(PropertyIds.Body, value));
                break;
            case TnefAttributes.MessageClass:
                legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.MessageClass, PstPropertyType.String8), null, Encoding.ASCII.GetBytes(MapMessageClass(Encoding.ASCII.GetString(TrimNull(value))))));
                break;
            case TnefAttributes.DateSent:
                AddDate(legacy, PropertyIds.ClientSubmitTime, value);
                break;
            case TnefAttributes.DateReceived:
                AddDate(legacy, PropertyIds.MessageDeliveryTime, value);
                break;
            case TnefAttributes.DateModified:
                AddDate(legacy, PropertyIds.LastModificationTime, value);
                break;
            case TnefAttributes.Priority when value.Length >= 2:
                // The priority is 1 (high), 2 (normal) or 3 (low); the importance is 2 (high), 1 (normal) or 0 (low)
                var importance = BinaryPrimitives.ReadUInt16LittleEndian(value) switch { 1 => 2, 3 => 0, _ => 1 };
                legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.Importance, PstPropertyType.Integer32), null, BitConverter.GetBytes(importance)));
                break;
            case TnefAttributes.From:
                ReadFrom(value, legacy);
                break;
        }
    }

    private static void ReadAttachmentAttribute(uint id, ReadOnlySpan<byte> value, List<TnefProperty> properties, List<TnefProperty> legacy, TnefContext context)
    {
        switch (id)
        {
            case TnefAttributes.Attachment:
                properties.AddRange(ReadPropertyList(value, context));
                break;
            case TnefAttributes.AttachData:
                legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.AttachDataBinary, PstPropertyType.Binary), null, value.ToArray()));
                legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.AttachMethod, PstPropertyType.Integer32), null, BitConverter.GetBytes((int)PstAttachmentMethod.ByValue)));
                break;
            case TnefAttributes.AttachTitle:
                legacy.Add(String8(PropertyIds.AttachFilename, value));
                break;
            case TnefAttributes.AttachCreateDate:
                AddDate(legacy, PropertyIds.CreationTime, value);
                break;
            case TnefAttributes.AttachModifyDate:
                AddDate(legacy, PropertyIds.LastModificationTime, value);
                break;
            case TnefAttributes.AttachRenderData when value.Length >= 6:
                legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.RenderingPosition, PstPropertyType.Integer32), null, value.Slice(2, 4).ToArray()));
                break;
        }
    }

    private static void ReadRecipientTable(ReadOnlySpan<byte> value, TnefItemData item, TnefContext context)
    {
        var position = 0;
        var rows = ReadCount(value, ref position, minimumSize: 4);
        for (var i = 0; i < rows; i++)
        {
            item.Recipients.Add(ReadProperties(value, ref position, context));
        }
    }

    private static List<TnefProperty> ReadPropertyList(ReadOnlySpan<byte> value, TnefContext context)
    {
        var position = 0;
        return ReadProperties(value, ref position, context);
    }

    /// <summary>Reads a list of MAPI properties ([MS-OXTNEF] 2.1.3.4): a count, then each property.</summary>
    private static List<TnefProperty> ReadProperties(ReadOnlySpan<byte> data, ref int position, TnefContext context)
    {
        var count = ReadCount(data, ref position, minimumSize: 8);
        var result = new List<TnefProperty>(count);
        for (var i = 0; i < count; i++)
        {
            var type = (PstPropertyType)ReadUInt16(data, ref position);
            var id = ReadUInt16(data, ref position);
            PstNamedProperty? name = null;
            if (id >= 0x8000)
            {
                name = ReadName(data, ref position);
                id = context.GetOrAddId(name);
            }

            var value = ReadValue(data, ref position, type, context);
            result.Add(new TnefProperty(new PstPropertyTag(id, type), name, value));
        }

        return result;
    }

    private static PstNamedProperty ReadName(ReadOnlySpan<byte> data, ref int position)
    {
        var propertySet = new Guid(ReadBytes(data, ref position, 16));
        var kind = ReadUInt32(data, ref position);
        if (kind == 0)
        {
            return new PstNamedProperty(propertySet, (int)ReadUInt32(data, ref position));
        }

        if (kind != 1)
        {
            throw new InvalidMailDataException($"A named property of the TNEF stream has an unknown kind {kind}.");
        }

        var length = (int)Math.Min(ReadUInt32(data, ref position), int.MaxValue);
        var name = ReadBytes(data, ref position, length);
        Skip(data, ref position, Padding(length));
        return new PstNamedProperty(propertySet, PropertyValueCodec.DecodeUnicode(name));
    }

    /// <summary>Reads a value, and returns it in the format of the values of an Outlook data file.</summary>
    private static byte[] ReadValue(ReadOnlySpan<byte> data, ref int position, PstPropertyType type, TnefContext context)
    {
        var elementType = PropertyTypes.GetElementType(type);
        var fixedSize = PropertyTypes.GetFixedSize(elementType);
        var isVariable = elementType is PstPropertyType.String8 or PstPropertyType.Unicode or PstPropertyType.Binary or PstPropertyType.ObjectReference;
        if (fixedSize == 0 && !isVariable)
        {
            throw new InvalidMailDataException($"A property of the TNEF stream has an unknown type 0x{(ushort)type:X4}.");
        }

        if (!PropertyTypes.IsMultiValued(type) && !isVariable)
        {
            return ReadFixed(data, ref position, fixedSize);
        }

        var count = ReadCount(data, ref position, minimumSize: 4);
        var values = new List<byte[]>(count);
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            byte[] value;
            if (isVariable)
            {
                var length = (int)Math.Min(ReadUInt32(data, ref position), int.MaxValue);
                value = PropertyValueCodec.RemoveTerminator(elementType, ReadBytes(data, ref position, length).ToArray());
                Skip(data, ref position, Padding(length));
            }
            else
            {
                value = ReadFixed(data, ref position, fixedSize);
            }

            total += value.Length;
            if (total > context.Options.MaxPropertySize)
            {
                throw new MailResourceLimitException($"A property of the TNEF stream is larger than the limit of {context.Options.MaxPropertySize} bytes.");
            }

            values.Add(value);
        }

        if (!PropertyTypes.IsMultiValued(type))
        {
            return values.Count > 0 ? values[0] : [];
        }

        return isVariable ? PropertyValueCodec.JoinVariable(values) : [.. values.SelectMany(value => value)];
    }

    /// <summary>Reads a value of a fixed-size type: the values of less than 4 bytes are padded to 4 bytes.</summary>
    private static byte[] ReadFixed(ReadOnlySpan<byte> data, ref int position, int size)
    {
        var stored = Math.Max(4, size);
        var value = ReadBytes(data, ref position, stored)[..size].ToArray();
        return value;
    }

    private static void ReadFrom(ReadOnlySpan<byte> value, List<TnefProperty> legacy)
    {
        // A TRP structure: its type, the size of the name and the address, the size of the name, the size of the address, then the name and the address ("SMTP:...")
        if (value.Length < 8)
        {
            return;
        }

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(value[4..]);
        var addressLength = BinaryPrimitives.ReadUInt16LittleEndian(value[6..]);
        if (8 + nameLength + addressLength > value.Length)
        {
            return;
        }

        legacy.Add(String8(PropertyIds.SenderName, value.Slice(8, nameLength)));
        var address = Encoding.Latin1.GetString(TrimNull(value.Slice(8 + nameLength, addressLength)));
        var colon = address.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0)
        {
            legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.SenderAddressType, PstPropertyType.String8), null, Encoding.Latin1.GetBytes(address[..colon])));
            legacy.Add(new TnefProperty(new PstPropertyTag(PropertyIds.SenderEmailAddress, PstPropertyType.String8), null, Encoding.Latin1.GetBytes(address[(colon + 1)..])));
        }
    }

    /// <summary>Returns the MAPI class of a message class of the old versions of Microsoft Mail ([MS-OXTNEF] 2.1.3.3.8).</summary>
    private static string MapMessageClass(string value) => value switch
    {
        "IPM.Microsoft Mail.Note" => "IPM.Note",
        "IPM.Microsoft Mail.Read Receipt" => "Report.IPM.Note.IPNRN",
        "IPM.Microsoft Mail.Non-Delivery" => "Report.IPM.Note.NDR",
        "IPM.Microsoft Schedule.MtgReq" => "IPM.Schedule.Meeting.Request",
        "IPM.Microsoft Schedule.MtgRespP" => "IPM.Schedule.Meeting.Resp.Pos",
        "IPM.Microsoft Schedule.MtgRespN" => "IPM.Schedule.Meeting.Resp.Neg",
        "IPM.Microsoft Schedule.MtgRespA" => "IPM.Schedule.Meeting.Resp.Tent",
        "IPM.Microsoft Schedule.MtgCncl" => "IPM.Schedule.Meeting.Canceled",
        _ => value,
    };

    private static void AddDate(List<TnefProperty> legacy, ushort id, ReadOnlySpan<byte> value)
    {
        // A DTR structure: the year, the month, the day, the hour, the minute, the second and the day of the week
        if (value.Length < 12)
        {
            return;
        }

        Span<int> parts = stackalloc int[6];
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = BinaryPrimitives.ReadUInt16LittleEndian(value[(i * 2)..]);
        }

        if (parts[0] is < 1601 or > 9999 || parts[1] is < 1 or > 12 || parts[2] < 1 || parts[2] > DateTime.DaysInMonth(parts[0], parts[1]) || parts[3] > 23 || parts[4] > 59 || parts[5] > 59)
        {
            return;
        }

        var date = new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], DateTimeKind.Utc);
        legacy.Add(new TnefProperty(new PstPropertyTag(id, PstPropertyType.Time), null, PropertyValueCodec.Encode(PstPropertyType.Time, date, Encoding.ASCII)));
    }

    private static TnefProperty String8(ushort id, ReadOnlySpan<byte> value) => new(new PstPropertyTag(id, PstPropertyType.String8), null, TrimNull(value).ToArray());

    private static ReadOnlySpan<byte> TrimNull(ReadOnlySpan<byte> value)
    {
        var end = value.IndexOf((byte)0);
        return end >= 0 ? value[..end] : value;
    }

    private static void AddMissing(List<TnefProperty> properties, List<TnefProperty> legacy)
    {
        foreach (var property in legacy)
        {
            if (!properties.Exists(existing => existing.Tag.Id == property.Tag.Id))
            {
                properties.Add(property);
            }
        }
    }

    private static int Padding(int length) => (4 - (length % 4)) % 4;

    /// <summary>Reads a count, and checks that the data can contain that many items of a minimum size.</summary>
    private static int ReadCount(ReadOnlySpan<byte> data, ref int position, int minimumSize)
    {
        var count = ReadUInt32(data, ref position);
        if (count > (uint)(data.Length - position) / (uint)minimumSize)
        {
            throw new InvalidMailDataException("A count of the TNEF stream is larger than its data.");
        }

        return (int)count;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int position) => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(data, ref position, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int position) => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(data, ref position, 4));

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> data, ref int position, int length)
    {
        if (length < 0 || length > data.Length - position)
        {
            throw new InvalidMailDataException("A property of the TNEF stream is truncated.");
        }

        var result = data.Slice(position, length);
        position += length;
        return result;
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int position, int length)
    {
        // Some writers omit the padding of the last value
        position = Math.Min(data.Length, position + length);
    }
}
