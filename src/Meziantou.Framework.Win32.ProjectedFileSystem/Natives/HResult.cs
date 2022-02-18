using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct HResult : IEquatable<HResult>
{
    public int Value { get; }

    public HResult(int value)
    {
        Value = value;
    }

    public bool IsSuccess => Value >= 0;

#pragma warning disable IDE1006 // Naming Styles
    public static HResult S_OK => new();
    public static HResult E_INVALIDARG => new(unchecked((int)0x80070057));
    public static HResult E_FILENOTFOUND => new(unchecked((int)0x80070002));
    public static HResult E_PATHNOTFOUND => new(unchecked((int)0x80070003));
    public static HResult E_OUTOFMEMORY => new(unchecked((int)0x8007000E));
    public static HResult ERROR_IO_PENDING => new(unchecked((int)0x800703E5));
    public static HResult ERROR_FILE_SYSTEM_VIRTUALIZATION_INVALID_OPERATION => new(unchecked((int)0x80070181));
#pragma warning restore IDE1006 // Naming Styles

    public void EnsureSuccess()
    {
        if (!IsSuccess)
        {
            Marshal.ThrowExceptionForHR(Value);
        }
    }

    public override bool Equals(object? obj) => obj is HResult result && Equals(result);
    public bool Equals(HResult other) => Value == other.Value;
    public override int GetHashCode() => -1937169414 + Value.GetHashCode();

    public static bool operator ==(HResult result1, HResult result2) => result1.Equals(result2);

    public static bool operator !=(HResult result1, HResult result2) => !(result1 == result2);
}
