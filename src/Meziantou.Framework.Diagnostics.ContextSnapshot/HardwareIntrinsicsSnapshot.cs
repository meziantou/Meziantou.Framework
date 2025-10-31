#pragma warning disable CA1822 // Mark members as static
#pragma warning disable CA2252 // This API requires opting into preview features
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of hardware intrinsics support at a specific point in time.</summary>
public sealed class HardwareIntrinsicsSnapshot
{
    internal HardwareIntrinsicsSnapshot() { }

    /// <summary>Gets the vector length in bits.</summary>
    /// <summary>Gets the vector length in bits.</summary>
    public int VectorLength => Vector<byte>.Count * 8;
    /// <summary>Gets a value indicating whether 64-bit vector operations are hardware accelerated.</summary>
    public bool IsVector64HardwareAccelerated => System.Runtime.Intrinsics.Vector64.IsHardwareAccelerated;
    /// <summary>Gets a value indicating whether 128-bit vector operations are hardware accelerated.</summary>
    public bool IsVector128HardwareAccelerated => System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated;
    /// <summary>Gets a value indicating whether 256-bit vector operations are hardware accelerated.</summary>
    public bool IsVector256HardwareAccelerated => System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated;
    /// <summary>Gets a value indicating whether 512-bit vector operations are hardware accelerated.</summary>
    public bool IsVector512HardwareAccelerated => System.Runtime.Intrinsics.Vector512.IsHardwareAccelerated;
    /// <summary>Gets a value indicating whether x86 AVX-512F instructions are supported.</summary>
    public bool IsX86Avx512FSupported => Avx512F.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX-512F VL instructions are supported.</summary>
    public bool IsX86Avx512FVLSupported => Avx512F.VL.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX-512BW instructions are supported.</summary>
    public bool IsX86Avx512BWSupported => Avx512BW.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX-512CD instructions are supported.</summary>
    public bool IsX86Avx512CDSupported => Avx512CD.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX-512DQ instructions are supported.</summary>
    public bool IsX86Avx512DQSupported => Avx512DQ.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX-512 VBMI instructions are supported.</summary>
    public bool IsX86Avx512VbmiSupported => Avx512Vbmi.IsSupported;

    /// <summary>Gets a value indicating whether WebAssembly base instructions are supported.</summary>
    public bool IsWasmBaseSupported => GetIsSupported("System.Runtime.Intrinsics.Wasm.WasmBase");

    /// <summary>Gets a value indicating whether WebAssembly packed SIMD instructions are supported.</summary>
    public bool IsWasmPackedSimdSupported => System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported;
    /// <summary>Gets a value indicating whether x86 base instructions are supported.</summary>
    public bool IsX86BaseSupported => X86Base.IsSupported;
    /// <summary>Gets a value indicating whether x86 SSE instructions are supported.</summary>
    public bool IsX86SseSupported => Sse.IsSupported;
    /// <summary>Gets a value indicating whether x86 SSE2 instructions are supported.</summary>
    public bool IsX86Sse2Supported => Sse2.IsSupported;
    /// <summary>Gets a value indicating whether x86 SSE3 instructions are supported.</summary>
    public bool IsX86Sse3Supported => Sse3.IsSupported;
    /// <summary>Gets a value indicating whether x86 SSSE3 instructions are supported.</summary>
    public bool IsX86Ssse3Supported => Ssse3.IsSupported;
    /// <summary>Gets a value indicating whether x86 SSE4.1 instructions are supported.</summary>
    public bool IsX86Sse41Supported => Sse41.IsSupported;
    /// <summary>Gets a value indicating whether x86 SSE4.2 instructions are supported.</summary>
    public bool IsX86Sse42Supported => Sse42.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX instructions are supported.</summary>
    public bool IsX86AvxSupported => Avx.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX2 instructions are supported.</summary>
    public bool IsX86Avx2Supported => Avx2.IsSupported;
    /// <summary>Gets a value indicating whether x86 AES instructions are supported.</summary>
    public bool IsX86AesSupported => System.Runtime.Intrinsics.X86.Aes.IsSupported;
    /// <summary>Gets a value indicating whether x86 BMI1 instructions are supported.</summary>
    public bool IsX86Bmi1Supported => Bmi1.IsSupported;
    /// <summary>Gets a value indicating whether x86 BMI2 instructions are supported.</summary>
    public bool IsX86Bmi2Supported => Bmi2.IsSupported;
    /// <summary>Gets a value indicating whether x86 FMA instructions are supported.</summary>
    public bool IsX86FmaSupported => Fma.IsSupported;
    /// <summary>Gets a value indicating whether x86 LZCNT instructions are supported.</summary>
    public bool IsX86LzcntSupported => Lzcnt.IsSupported;
    /// <summary>Gets a value indicating whether x86 PCLMULQDQ instructions are supported.</summary>
    public bool IsX86PclmulqdqSupported => Pclmulqdq.IsSupported;
    /// <summary>Gets a value indicating whether x86 POPCNT instructions are supported.</summary>
    public bool IsX86PopcntSupported => Popcnt.IsSupported;
    /// <summary>Gets a value indicating whether x86 AVX-VNNI instructions are supported.</summary>
    public bool IsX86AvxVnniSupported => AvxVnni.IsSupported;
    /// <summary>Gets a value indicating whether x86 SERIALIZE instructions are supported.</summary>
    public bool IsX86SerializeSupported => X86Serialize.IsSupported;
    /// <summary>Gets a value indicating whether ARM base instructions are supported.</summary>
    public bool IsArmBaseSupported => ArmBase.IsSupported;
    /// <summary>Gets a value indicating whether ARM Advanced SIMD instructions are supported.</summary>
    public bool IsArmAdvSimdSupported => AdvSimd.IsSupported;
    /// <summary>Gets a value indicating whether ARM AES instructions are supported.</summary>
    public bool IsArmAesSupported => System.Runtime.Intrinsics.Arm.Aes.IsSupported;
    /// <summary>Gets a value indicating whether ARM CRC32 instructions are supported.</summary>
    public bool IsArmCrc32Supported => Crc32.IsSupported;
    /// <summary>Gets a value indicating whether ARM dot product instructions are supported.</summary>
    public bool IsArmDpSupported => Dp.IsSupported;
    /// <summary>Gets a value indicating whether ARM rounding double multiply instructions are supported.</summary>
    public bool IsArmRdmSupported => Rdm.IsSupported;
    /// <summary>Gets a value indicating whether ARM SHA1 instructions are supported.</summary>
    public bool IsArmSha1Supported => Sha1.IsSupported;
    /// <summary>Gets a value indicating whether ARM SHA256 instructions are supported.</summary>
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
