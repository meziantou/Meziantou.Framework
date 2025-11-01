namespace Meziantou.Framework.Win32;

/// <summary>
/// Specifies the reasons for changes in a change journal entry.
/// </summary>
[Flags]
public enum ChangeReason : uint
{
    /// <summary>A file or directory's basic information changed (e.g., timestamps or attributes).</summary>
    BasicInfoChange = 0x00008000,

    /// <summary>The file or directory was closed.</summary>
    Close = 0x80000000,

    /// <summary>The file or directory's compression state changed.</summary>
    CompressionChange = 0x00020000,

    /// <summary>Data was added to the file.</summary>
    DataExtend = 0x00000002,

    /// <summary>Data in the file was overwritten.</summary>
    DataOverwrite = 0x00000001,

    /// <summary>The file was truncated.</summary>
    DataTruncation = 0x00000004,

    /// <summary>The file or directory's extended attributes changed.</summary>
    ExtendedAttributesChange = 0x00000400,

    /// <summary>The file or directory's encryption state changed.</summary>
    EncryptionChange = 0x00040000,

    /// <summary>The file or directory was created.</summary>
    FileCreate = 0x00000100,

    /// <summary>The file or directory was deleted.</summary>
    FileDelete = 0x00000200,

    /// <summary>A hard link was added or removed.</summary>
    HardLinkChange = 0x00010000,

    /// <summary>The file or directory's indexing state changed.</summary>
    IndexableChange = 0x00004000,

    /// <summary>A named data stream was extended.</summary>
    NamedDataExtend = 0x00000020,

    /// <summary>Data in a named data stream was overwritten.</summary>
    NamedDataOverwrite = 0x00000010,

    /// <summary>A named data stream was truncated.</summary>
    NamedDataTruncation = 0x00000040,

    /// <summary>The object identifier changed.</summary>
    ObjectIDChange = 0x00080000,

    /// <summary>The file or directory was renamed, and this is the new name.</summary>
    RenameNewName = 0x00002000,

    /// <summary>The file or directory was renamed, and this is the old name.</summary>
    RenameOldName = 0x00001000,

    /// <summary>The reparse point contained in the file or directory changed.</summary>
    ReparsePointChange = 0x00100000,

    /// <summary>The file or directory's security descriptor changed.</summary>
    SecurityChange = 0x00000800,

    /// <summary>A named stream was added to or removed from the file.</summary>
    StreamChange = 0x00200000,

    /// <summary>The file or directory changed as a result of a transacted operation.</summary>
    TransactedChange = 0x00400000,

    /// <summary>All change reasons combined.</summary>
    All = BasicInfoChange | Close | CompressionChange | DataExtend | DataOverwrite | DataTruncation | ExtendedAttributesChange | EncryptionChange | FileCreate | FileDelete | HardLinkChange | IndexableChange | NamedDataExtend | NamedDataOverwrite | NamedDataTruncation | ObjectIDChange | RenameNewName | RenameOldName | ReparsePointChange | StreamChange | SecurityChange | TransactedChange,
}
