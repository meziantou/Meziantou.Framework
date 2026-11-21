namespace Meziantou.Framework.Mail.Internals;

/// <summary>Copies the properties of objects read from a file to builders.</summary>
internal static class PstCopier
{
    private const int StreamThreshold = 64 * 1024;

    /// <summary>The properties the file computes from its structure, which the writer computes again ([MS-PST] 2.5.2).</summary>
    public static readonly HashSet<ushort> ComputedMessageProperties = [PropertyIds.EntryId, 0x0E09, PropertyIds.HasAttachments, PropertyIds.MessageSize, PropertyIds.LtpRowId, PropertyIds.LtpRowVersion, 0x0FFB, 0x0FFE];

    public static readonly HashSet<ushort> ComputedRecipientProperties = [PropertyIds.LtpRowId, PropertyIds.LtpRowVersion];

    public static readonly HashSet<ushort> ComputedAttachmentProperties = [PropertyIds.AttachNumber, PropertyIds.LtpRowId, PropertyIds.LtpRowVersion, PropertyIds.EntryId, 0x0FFE];

    public static void CopyProperties(PstPropertyCollection source, PstPropertyBag destination, HashSet<ushort> excluded)
    {
        foreach (var property in source)
        {
            if (excluded.Contains(property.Id) || property.Type == PstPropertyType.ObjectReference)
            {
                continue;
            }

            PstNamedProperty? name = null;
            if (property.Tag.IsNamed)
            {
                name = property.NamedProperty;
                if (name is null)
                {
                    // A named property the file does not map cannot be written with another identifier
                    continue;
                }
            }

            if (property.Type == PstPropertyType.Binary && property.GetSize() > StreamThreshold)
            {
                // The writer opens a stream on the source file each time it writes the item, so the caller keeps the source open until then
                destination.SetStream(property.Tag, name, property.OpenRead);
                continue;
            }

            if (property.Type is PstPropertyType.String8 or PstPropertyType.MultipleString8)
            {
                // The 8-bit strings are in the code page of the source, which the destination does not know: they are copied as Unicode strings
                var unicode = property.Type == PstPropertyType.String8 ? PstPropertyType.Unicode : PstPropertyType.MultipleUnicode;
                object? value;
                try
                {
                    value = property.Value;
                }
                catch (PstException)
                {
                    value = null;
                }

                if (value is not null)
                {
                    destination.SetRaw(new PstPropertyTag(property.Id, unicode), name, Ltp.PropertyValueCodec.Encode(unicode, value, source.Encoding));
                    continue;
                }
            }

            destination.SetRaw(property.Tag, name, property.GetBytes());
        }
    }
}
