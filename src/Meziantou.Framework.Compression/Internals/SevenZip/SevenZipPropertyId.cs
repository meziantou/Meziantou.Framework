namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The property IDs of the headers.</summary>
internal static class SevenZipPropertyId
{
    public const byte End = 0x00;
    public const byte Header = 0x01;
    public const byte ArchiveProperties = 0x02;
    public const byte AdditionalStreamsInfo = 0x03;
    public const byte MainStreamsInfo = 0x04;
    public const byte FilesInfo = 0x05;
    public const byte PackInfo = 0x06;
    public const byte UnpackInfo = 0x07;
    public const byte SubStreamsInfo = 0x08;
    public const byte Size = 0x09;
    public const byte Crc = 0x0A;
    public const byte Folder = 0x0B;
    public const byte CodersUnpackSize = 0x0C;
    public const byte NumUnpackStream = 0x0D;
    public const byte EmptyStream = 0x0E;
    public const byte EmptyFile = 0x0F;
    public const byte Anti = 0x10;
    public const byte Name = 0x11;
    public const byte CTime = 0x12;
    public const byte ATime = 0x13;
    public const byte MTime = 0x14;
    public const byte WinAttributes = 0x15;
    public const byte Comment = 0x16;
    public const byte EncodedHeader = 0x17;
    public const byte StartPos = 0x18;
    public const byte Dummy = 0x19;
}
