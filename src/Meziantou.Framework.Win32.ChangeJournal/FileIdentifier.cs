using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a unique file or directory identifier on an NTFS volume.</summary>
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

    /// <summary>Gets the file identifier for the specified file or directory path.</summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <returns>The file identifier for the specified file or directory.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe static FileIdentifier FromFile(string path)
    {
        using var handle = File.OpenHandle(path);
        return FromFile(handle);
    }

    /// <summary>Gets the file identifier for the specified file or directory handle.</summary>
    /// <param name="handle">A handle to the file or directory.</param>
    /// <returns>The file identifier for the specified file or directory.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe static FileIdentifier FromFile(SafeFileHandle handle)
    {
        var result = new FILE_ID_INFO();
        using var handleScope = new SafeHandleValue(handle);
        if (PInvoke.GetFileInformationByHandleEx((HANDLE)handleScope.Value, FILE_INFO_BY_HANDLE_CLASS.FileIdInfo, &result, (uint)sizeof(FILE_ID_INFO)))
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
