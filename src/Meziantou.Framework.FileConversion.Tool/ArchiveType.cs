namespace Meziantou.Framework.FileConversionTool;

/// <summary>The container of an archive or a comic book archive: its archive format and the compression around it.</summary>
internal enum ArchiveType
{
    None,
    Zip,
    SevenZip,
    Rar,
    Tar,
    TarGzip,
    TarBzip2,
    TarXz,
    TarLzma,
    TarLzip,
    TarZstandard,
    TarUnixCompress,
}
