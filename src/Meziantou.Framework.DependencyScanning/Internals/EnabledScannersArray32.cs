using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EnabledScannersArray32 : IEnabledScannersArray
    {
        public static int MaxValues { get; } = Marshal.SizeOf<EnabledScannersArray32>() * 8; // Number of bits

        private uint _value;

        public bool IsEmpty => _value == 0;

        public void Set(int index) => _value |= 1u << index;

        public bool Get(int index) => (_value & (1u << index)) != 0;
    }
}
