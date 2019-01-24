using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct HResult : IEquatable<HResult>
    {
        public int Value { get; }

        public HResult(int value)
        {
            Value = value;
        }

        public bool IsSuccess => Value >= 0;

        public static HResult S_OK => new HResult();
        public static HResult E_INVALIDARG => new HResult(unchecked((int)0x80070057));
        public static HResult E_FILENOTFOUND => new HResult(unchecked((int)0x80070002));
        public static HResult E_PATHNOTFOUND => new HResult(unchecked((int)0x80070003));
        public static HResult E_OUTOFMEMORY => new HResult(unchecked((int)0x8007000E));
        public static HResult ERROR_IO_PENDING => new HResult(unchecked((int)0x800703E5));
        public static HResult ERROR_FILE_SYSTEM_VIRTUALIZATION_INVALID_OPERATION => new HResult(unchecked((int)0x80070181));

        public void EnsureSuccess()
        {
            if (!IsSuccess)
            {
                Marshal.ThrowExceptionForHR(Value);
            }
        }

        public override bool Equals(object obj)
        {
            return obj is HResult && Equals((HResult)obj);
        }

        public bool Equals(HResult other)
        {
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return -1937169414 + Value.GetHashCode();
        }

        public static bool operator ==(HResult result1, HResult result2) => result1.Equals(result2);

        public static bool operator !=(HResult result1, HResult result2) => !(result1 == result2);
    }
}
