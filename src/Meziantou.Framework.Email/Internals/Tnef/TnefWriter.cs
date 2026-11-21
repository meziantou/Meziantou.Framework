using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Msg;

namespace Meziantou.Framework.Email.Internals.Tnef;

/// <summary>Writes an item as a TNEF stream ([MS-OXTNEF]): its MAPI properties in attMsgProps, its recipients in attRecipTable, and an attAttachment per attachment, with Unicode strings.</summary>
internal static class TnefWriter
{
    private const int CodePage = 1252;

    public static void Write(MapiMessageBuilder message, Stream output)
    {
        var encoder = new ItemEncoder();
        WriteItem(output, encoder.Encode(message), encoder);
    }

    private static void WriteItem(Stream output, EncodedItem item, ItemEncoder encoder)
    {
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(header, TnefAttributes.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 0x0001); // The legacy key of the attachments
        output.Write(header);

        WriteAttribute(output, TnefAttributes.MessageLevel, TnefAttributes.TnefVersion, BitConverter.GetBytes(0x00010000));
        WriteAttribute(output, TnefAttributes.MessageLevel, TnefAttributes.OemCodepage, [.. BitConverter.GetBytes(CodePage), .. BitConverter.GetBytes(0)]);
        if (item.Properties.Find(property => property.Tag.Id == PropertyIds.MessageClass) is { Value: { } messageClass } property)
        {
            // The class in the attribute of the old versions of Microsoft Mail, which some readers need
            var value = PropertyValueCodec.Decode(property.Tag.Type, messageClass, encoder.String8Encoding) as string ?? "IPM.Note";
            WriteAttribute(output, TnefAttributes.MessageLevel, TnefAttributes.MessageClass, [.. encoder.String8Encoding.GetBytes(value), 0]);
        }

        if (item.Recipients.Count > 0)
        {
            using var table = new MemoryStream();
            WriteUInt32(table, (uint)item.Recipients.Count);
            foreach (var recipient in item.Recipients)
            {
                WriteProperties(table, recipient, encoder);
            }

            WriteAttribute(output, TnefAttributes.MessageLevel, TnefAttributes.RecipientTable, table.ToArray());
        }

        using (var properties = new MemoryStream())
        {
            WriteProperties(properties, item.Properties, encoder);
            WriteAttribute(output, TnefAttributes.MessageLevel, TnefAttributes.MessageProperties, properties.ToArray());
        }

        foreach (var attachment in item.Attachments)
        {
            // attAttachRenddata starts the attachment: a file, at no position in the body
            var render = new byte[14];
            BinaryPrimitives.WriteUInt16LittleEndian(render, 1);
            BinaryPrimitives.WriteInt32LittleEndian(render.AsSpan(2), -1);
            WriteAttribute(output, TnefAttributes.AttachmentLevel, TnefAttributes.AttachRenderData, render);

            var attachmentProperties = attachment.Properties;
            if (attachment.EmbeddedMessage is { } embedded)
            {
                using var nested = new MemoryStream();
                nested.Write(TnefAttributes.MessageInterface.ToByteArray());
                WriteItem(nested, embedded, encoder);
                attachmentProperties = [.. attachmentProperties, new WriterProperty(new MapiPropertyTag(ItemEncoder.AttachDataObjectId, MapiPropertyType.ObjectReference), nested.ToArray())];
            }

            using var stream = new MemoryStream();
            WriteProperties(stream, attachmentProperties, encoder);
            WriteAttribute(output, TnefAttributes.AttachmentLevel, TnefAttributes.Attachment, stream.ToArray());
        }
    }

    private static void WriteAttribute(Stream output, byte level, uint id, byte[] value)
    {
        Span<byte> header = stackalloc byte[9];
        header[0] = level;
        BinaryPrimitives.WriteUInt32LittleEndian(header[1..], id);
        BinaryPrimitives.WriteInt32LittleEndian(header[5..], value.Length);
        output.Write(header);
        output.Write(value);
        Span<byte> checksum = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(checksum, TnefParser.Checksum(value));
        output.Write(checksum);
    }

    /// <summary>Writes a list of MAPI properties ([MS-OXTNEF] 2.1.3.4): a count, then each property with its name for a named property.</summary>
    private static void WriteProperties(Stream output, List<WriterProperty> properties, ItemEncoder encoder)
    {
        WriteUInt32(output, (uint)properties.Count);
        foreach (var property in properties)
        {
            var tag = property.Tag;
            WriteUInt16(output, (ushort)tag.Type);
            WriteUInt16(output, tag.Id);
            if (tag.IsNamed)
            {
                var name = encoder.Names.GetName(tag.Id);
                output.Write(name.PropertySet.ToByteArray());
                if (name.Name is { } stringName)
                {
                    WriteUInt32(output, 1);
                    WriteVariable(output, [.. PropertyValueCodec.EncodeUnicode(stringName), 0, 0]);
                }
                else
                {
                    WriteUInt32(output, 0);
                    WriteUInt32(output, (uint)name.Id!.Value);
                }
            }

            WriteValue(output, tag.Type, property.Value ?? []);
        }
    }

    private static void WriteValue(Stream output, MapiPropertyType type, byte[] value)
    {
        var elementType = PropertyTypes.GetElementType(type);
        var fixedSize = PropertyTypes.GetFixedSize(elementType);
        if (!PropertyTypes.IsMultiValued(type))
        {
            if (fixedSize > 0)
            {
                WritePadded(output, value.AsSpan(0, Math.Min(value.Length, fixedSize)), fixedSize);
            }
            else
            {
                WriteUInt32(output, 1);
                WriteVariable(output, MsgWriter.AddTerminator(type, value));
            }

            return;
        }

        if (fixedSize > 0)
        {
            var count = value.Length / fixedSize;
            WriteUInt32(output, (uint)count);
            for (var i = 0; i < count; i++)
            {
                WritePadded(output, value.AsSpan(i * fixedSize, fixedSize), fixedSize);
            }

            return;
        }

        var values = PropertyValueCodec.SplitVariable(value);
        WriteUInt32(output, (uint)values.Count);
        foreach (var item in values)
        {
            WriteVariable(output, MsgWriter.AddTerminator(elementType, item));
        }
    }

    /// <summary>Writes a value of a fixed-size type, padded to 4 bytes.</summary>
    private static void WritePadded(Stream output, ReadOnlySpan<byte> value, int size)
    {
        var buffer = new byte[Math.Max(4, size)];
        value.CopyTo(buffer);
        output.Write(buffer);
    }

    /// <summary>Writes a value of a variable-size type: its size, its bytes, then the padding to 4 bytes.</summary>
    private static void WriteVariable(Stream output, byte[] value)
    {
        WriteUInt32(output, (uint)value.Length);
        output.Write(value);
        output.Write(new byte[(4 - (value.Length % 4)) % 4]);
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        output.Write(buffer);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        output.Write(buffer);
    }
}
