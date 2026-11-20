namespace Meziantou.Framework.Mail.Internals.Ndb;

/// <summary>The special node identifiers ([MS-PST] 2.4.1) and helpers on node identifiers.</summary>
internal static class Nids
{
    public const uint MessageStore = 0x21;
    public const uint NameToIdMap = 0x61;
    public const uint NormalFolderTemplate = 0xA1;
    public const uint SearchFolderTemplate = 0xC1;
    public const uint RootFolder = 0x122;
    public const uint SearchManagementQueue = 0x1E1;
    public const uint SearchActivityList = 0x201;
    public const uint HierarchyTableTemplate = 0x60D;
    public const uint ContentsTableTemplate = 0x60E;
    public const uint AssociatedContentsTableTemplate = 0x60F;
    public const uint SearchContentsTableTemplate = 0x610;
    public const uint AttachmentTable = 0x671;
    public const uint RecipientTable = 0x692;

    public static uint Make(NidType type, uint index) => (index << 5) | (uint)type;

    public static uint GetIndex(uint nid) => nid >> 5;

    public static uint WithType(uint nid, NidType type) => (nid & ~0x1Fu) | (uint)type;
}
