namespace Meziantou.Framework.Compression.Internals.Rar;

internal enum RarRedirectionType
{
    None = 0,
    UnixSymlink = 1,
    WindowsSymlink = 2,
    WindowsJunction = 3,
    HardLink = 4,
    FileCopy = 5,
}
