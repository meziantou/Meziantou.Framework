#pragma warning disable CA1822 // Mark members as static
#pragma warning disable CA2252 // This API requires opting into preview features
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class HardwareIntrinsicsSnapshot
{
    internal HardwareIntrinsicsSnapshot() { }

    public int VectorLength => Vector<byte>.Count * 8;
    public bool IsVector64HardwareAccelerated => System.Runtime.Intrinsics.Vector64.IsHardwareAccelerated;
    public bool IsVector128HardwareAccelerated => System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated;
    public bool IsVector256HardwareAccelerated => System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated;
    public bool IsVector512HardwareAccelerated => System.Runtime.Intrinsics.Vector512.IsHardwareAccelerated;
    public bool IsX86Avx512FSupported => Avx512F.IsSupported;
    public bool IsX86Avx512FVLSupported => Avx512F.VL.IsSupported;
    public bool IsX86Avx512BWSupported => Avx512BW.IsSupported;
    public bool IsX86Avx512CDSupported => Avx512CD.IsSupported;
    public bool IsX86Avx512DQSupported => Avx512DQ.IsSupported;
    public bool IsX86Avx512VbmiSupported => Avx512Vbmi.IsSupported;

    public bool IsWasmBaseSupported => GetIsSupported("System.Runtime.Intrinsics.Wasm.WasmBase");

    public bool IsWasmPackedSimdSupported => System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported;
    public bool IsX86BaseSupported => X86Base.IsSupported;
    public bool IsX86SseSupported => Sse.IsSupported;
    public bool IsX86Sse2Supported => Sse2.IsSupported;
    public bool IsX86Sse3Supported => Sse3.IsSupported;
    public bool IsX86Ssse3Supported => Ssse3.IsSupported;
    public bool IsX86Sse41Supported => Sse41.IsSupported;
    public bool IsX86Sse42Supported => Sse42.IsSupported;
    public bool IsX86AvxSupported => Avx.IsSupported;
    public bool IsX86Avx2Supported => Avx2.IsSupported;
    public bool IsX86AesSupported => System.Runtime.Intrinsics.X86.Aes.IsSupported;
    public bool IsX86Bmi1Supported => Bmi1.IsSupported;
    public bool IsX86Bmi2Supported => Bmi2.IsSupported;
    public bool IsX86FmaSupported => Fma.IsSupported;
    public bool IsX86LzcntSupported => Lzcnt.IsSupported;
    public bool IsX86PclmulqdqSupported => Pclmulqdq.IsSupported;
    public bool IsX86PopcntSupported => Popcnt.IsSupported;
    public bool IsX86AvxVnniSupported => AvxVnni.IsSupported;
    public bool IsX86SerializeSupported => X86Serialize.IsSupported;
    public bool IsArmBaseSupported => ArmBase.IsSupported;
    public bool IsArmAdvSimdSupported => AdvSimd.IsSupported;
    public bool IsArmAesSupported => System.Runtime.Intrinsics.Arm.Aes.IsSupported;
    public bool IsArmCrc32Supported => Crc32.IsSupported;
    public bool IsArmDpSupported => Dp.IsSupported;
    public bool IsArmRdmSupported => Rdm.IsSupported;
    public bool IsArmSha1Supported => Sha1.IsSupported;
    public bool IsArmSha256Supported => Sha256.IsSupported;

    private static bool GetIsSupported([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: false);
        if (type == null)
            return false;

        var property = type.GetProperty("IsSupported", BindingFlags.Public | BindingFlags.Static);
        if (property == null)
            return false;

        if (property.GetValue(null, null) is bool value)
            return value;

        return false;
    }
}
