namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>The type of a node, the low 5 bits of its identifier ([MS-PST] 2.2.2.1).</summary>
internal enum NidType : byte
{
    HeapNode = 0x00,
    Internal = 0x01,
    NormalFolder = 0x02,
    SearchFolder = 0x03,
    NormalMessage = 0x04,
    Attachment = 0x05,
    SearchUpdateQueue = 0x06,
    SearchCriteriaObject = 0x07,
    AssociatedMessage = 0x08,
    ContentsTableIndex = 0x0A,
    ReceiveFolderTable = 0x0B,
    OutgoingQueueTable = 0x0C,
    HierarchyTable = 0x0D,
    ContentsTable = 0x0E,
    AssociatedContentsTable = 0x0F,
    SearchContentsTable = 0x10,
    AttachmentTable = 0x11,
    RecipientTable = 0x12,
    SearchTableIndex = 0x13,
    Ltp = 0x1F,
}
