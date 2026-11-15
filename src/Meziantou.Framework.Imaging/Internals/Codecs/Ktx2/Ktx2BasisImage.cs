using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An image descriptor of the BasisLZ global data (KTX Appendix C.7).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Ktx2BasisImage(uint Flags, uint RgbOffset, uint RgbLength, uint AlphaOffset, uint AlphaLength)
{
    public bool IsPFrame => (Flags & Ktx2Format.BasisLzPFrameFlag) != 0;
}
