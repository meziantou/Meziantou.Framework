using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EnabledScannersArray64 : IEnabledScannersArray
    {
        public static int MaxValues { get; } = Marshal.SizeOf<EnabledScannersArray64>() * 8; // Number of bits

        private ulong _value;

        public bool IsEmpty => _value == 0;

        public void Set(int index) => _value |= 1ul << index;

        public bool Get(int index) => (_value & (1ul << index)) != 0ul;
    }
}
