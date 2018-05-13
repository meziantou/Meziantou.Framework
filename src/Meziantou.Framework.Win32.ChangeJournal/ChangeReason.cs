using System;

namespace Meziantou.Framework.Win32
{
    [Flags]
    public enum ChangeReason : uint
    {
        BasicInfoChange = 0x00008000,
        Close = 0x80000000,
        CompressionChange = 0x00020000,
        DataExtend = 0x00000002,
        DataOverwrite = 0x00000001,
        DataTruncation = 0x00000004,
        ExtendedAttributesChange = 0x00000400,
        EncryptionChange = 0x00040000,
        FileCreate = 0x00000100,
        FileDelete = 0x00000200,
        HardLinkChange = 0x00010000,
        IndexableChange = 0x00004000,
        NamedDataExtend = 0x00000020,
        NamedDataOverwrite = 0x00000010,
        NamedDataTruncation = 0x00000040,
        ObjectIDChange = 0x00080000,
        RenameNewName = 0x00002000,
        RenameOldName = 0x00001000,
        ReparsePointChange = 0x00100000,
        SecurityChange = 0x00000800,
        StreamChange = 0x00200000,

        All = BasicInfoChange | Close | CompressionChange | DataExtend | DataOverwrite | DataTruncation | ExtendedAttributesChange | EncryptionChange | FileCreate | FileDelete | HardLinkChange | IndexableChange | NamedDataExtend | NamedDataOverwrite | NamedDataTruncation | ObjectIDChange | RenameNewName | RenameOldName | ReparsePointChange | StreamChange | SecurityChange
    }
}