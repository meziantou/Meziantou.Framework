using System.Globalization;

namespace Meziantou.Framework.Email.Internals.Msg;

/// <summary>The names of the storages and streams of an .msg file ([MS-OXMSG] 2.2).</summary>
internal static class MsgNames
{
    public const string Properties = "__properties_version1.0";
    public const string NameId = "__nameid_version1.0";
    public const string RecipientPrefix = "__recip_version1.0_#";
    public const string AttachmentPrefix = "__attach_version1.0_#";
    public const string StreamPrefix = "__substg1.0_";
    public const ushort AttachDataObjectId = 0x3701;
    public const int BucketCount = 0x1F;

    /// <summary>The class identifier of the root storage of an .msg file (CLSID_MailMessage).</summary>
    public static readonly Guid MessageClsid = new("00020D0B-0000-0000-C000-000000000046");

    /// <summary>The class identifier of the root storage of an Outlook template, an .oft file (CLSID_TemplateMessage).</summary>
    public static readonly Guid TemplateClsid = new("0006F046-0000-0000-C000-000000000046");

    public static string Recipient(int index) => RecipientPrefix + index.ToString("X8", CultureInfo.InvariantCulture);

    public static string Attachment(int index) => AttachmentPrefix + index.ToString("X8", CultureInfo.InvariantCulture);

    public static string Stream(MapiPropertyTag tag) => StreamPrefix + tag.Value.ToString("X8", CultureInfo.InvariantCulture);

    public static string Stream(MapiPropertyTag tag, int index) => Stream(tag) + "-" + index.ToString("X8", CultureInfo.InvariantCulture);

    public static string EmbeddedMessage => Stream(new MapiPropertyTag(AttachDataObjectId, MapiPropertyType.ObjectReference));

    /// <summary>Returns the tag of a stream named <c>__substg1.0_TTTTTTTT</c>.</summary>
    public static bool TryParseStream(string name, out MapiPropertyTag tag)
    {
        if (name.Length == StreamPrefix.Length + 8 && name.StartsWith(StreamPrefix, StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(name.AsSpan(StreamPrefix.Length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            tag = MapiPropertyTag.FromValue(value);
            return true;
        }

        tag = default;
        return false;
    }
}
