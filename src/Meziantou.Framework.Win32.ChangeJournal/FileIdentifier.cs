using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework.Win32;

[StructLayout(LayoutKind.Auto)]
public readonly struct FileIdentifier : IEquatable<FileIdentifier>
{
    private readonly bool _isVersion3;
    private readonly UInt128 _value;

    public FileIdentifier(UInt128 value)
    {
        _value = value;
        _isVersion3 = true;
    }

    public FileIdentifier(ulong value)
    {
        _value = value;
        _isVersion3 = false;
    }

    internal FileIdentifier(FILE_ID_128 parentFileReferenceNumber)
        : this(Unsafe.BitCast<FILE_ID_128, UInt128>(parentFileReferenceNumber))
    {
    }

    public bool Equals(FileIdentifier other) => _value == other._value;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FileIdentifier identifier && Equals(identifier);
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => _value.ToString(_isVersion3 ? "x32" : "x16", CultureInfo.InvariantCulture);

    public static bool operator ==(FileIdentifier left, FileIdentifier right) => left.Equals(right);
    public static bool operator !=(FileIdentifier left, FileIdentifier right) => !(left == right);
}
