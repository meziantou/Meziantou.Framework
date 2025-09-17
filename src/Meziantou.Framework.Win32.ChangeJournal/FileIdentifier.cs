using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework.Win32;

[StructLayout(LayoutKind.Auto)]
public readonly struct FileIdentifier : IEquatable<FileIdentifier>
{
    private readonly bool _is128Bits;
    private readonly UInt128 _value;

    public FileIdentifier(UInt128 value)
    {
        _value = value;
        _is128Bits = true;
    }

    public FileIdentifier(ulong value)
    {
        _value = value;
        _is128Bits = false;
    }

    internal FileIdentifier(FILE_ID_128 parentFileReferenceNumber)
        : this(Unsafe.BitCast<FILE_ID_128, UInt128>(parentFileReferenceNumber))
    {
    }

    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe static FileIdentifier FromFile(string path)
    {
        using var handle = File.OpenHandle(path);
        return FromFile(handle);
    }

    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe static FileIdentifier FromFile(SafeFileHandle handle)
    {
        var result = new FILE_ID_INFO();
        FILE_ID_INFO* pointer = &result;
        if (PInvoke.GetFileInformationByHandleEx(handle, FILE_INFO_BY_HANDLE_CLASS.FileIdInfo, pointer, (uint)sizeof(FILE_ID_INFO)))
        {
            return new FileIdentifier(result.FileId);
        }

        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public bool Equals(FileIdentifier other) => _value == other._value;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FileIdentifier identifier && Equals(identifier);
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => _value.ToString(_is128Bits ? "x32" : "x16", CultureInfo.InvariantCulture);

    public static bool operator ==(FileIdentifier left, FileIdentifier right) => left.Equals(right);
    public static bool operator !=(FileIdentifier left, FileIdentifier right) => !(left == right);
}
