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

#if NET7_0_OR_GREATER
    public bool IsVector64HardwareAccelerated =>
        System.Runtime.Intrinsics.Vector64.IsHardwareAccelerated;

    public bool IsVector128HardwareAccelerated =>
        System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated;

    public bool IsVector256HardwareAccelerated =>
        System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated;
#endif

#if NET8_0_OR_GREATER
    public bool IsVector512HardwareAccelerated =>
        System.Runtime.Intrinsics.Vector512.IsHardwareAccelerated;
#endif

    public bool IsWasmBaseSupported =>
        GetIsSupported("System.Runtime.Intrinsics.Wasm.WasmBase");

    public bool IsWasmPackedSimdSupported =>
#if NET8_0_OR_GREATER
        System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Wasm.PackedSimd");
#endif

    public bool IsX86BaseSupported =>
#if NET6_0_OR_GREATER
        X86Base.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.X86Base");
#endif

    public bool IsX86SseSupported =>
#if NET6_0_OR_GREATER
        Sse.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Sse");
#endif

    public bool IsX86Sse2Supported =>
#if NET6_0_OR_GREATER
        Sse2.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Sse2");
#endif

    public bool IsX86Sse3Supported =>
#if NET6_0_OR_GREATER
        Sse3.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Sse3");
#endif

    public bool IsX86Ssse3Supported =>
#if NET6_0_OR_GREATER
        Ssse3.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Ssse3");
#endif

    public bool IsX86Sse41Supported =>
#if NET6_0_OR_GREATER
        Sse41.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Sse41");
#endif

    public bool IsX86Sse42Supported =>
#if NET6_0_OR_GREATER
        Sse42.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Sse42");
#endif

    public bool IsX86AvxSupported =>
#if NET6_0_OR_GREATER
        Avx.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Avx");
#endif

    public bool IsX86Avx2Supported =>
#if NET6_0_OR_GREATER
        Avx2.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Avx2");
#endif

    public bool IsX86AesSupported =>
#if NET6_0_OR_GREATER
        System.Runtime.Intrinsics.X86.Aes.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Aes");
#endif

    public bool IsX86Bmi1Supported =>
#if NET6_0_OR_GREATER
        Bmi1.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Bmi1");
#endif

    public bool IsX86Bmi2Supported =>
#if NET6_0_OR_GREATER
        Bmi2.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Bmi2");
#endif

    public bool IsX86FmaSupported =>
#if NET6_0_OR_GREATER
        Fma.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Fma");
#endif

    public bool IsX86LzcntSupported =>
#if NET6_0_OR_GREATER
        Lzcnt.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Lzcnt");
#endif

    public bool IsX86PclmulqdqSupported =>
#if NET6_0_OR_GREATER
        Pclmulqdq.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Pclmulqdq");
#endif

    public bool IsX86PopcntSupported =>
#if NET6_0_OR_GREATER
        Popcnt.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.Popcnt");
#endif

    public bool IsX86AvxVnniSupported =>
#if NET6_0_OR_GREATER
        AvxVnni.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.AvxVnni");
#endif

    public bool IsX86SerializeSupported =>
#if NET7_0_OR_GREATER
        X86Serialize.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.X86.X86Serialize");
#endif


    public bool IsArmBaseSupported =>
#if NET6_0_OR_GREATER
        ArmBase.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.ArmBase");
#endif

    public bool IsArmAdvSimdSupported =>
#if NET6_0_OR_GREATER
        AdvSimd.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.AdvSimd");
#endif

    public bool IsArmAesSupported =>
#if NET6_0_OR_GREATER
        System.Runtime.Intrinsics.Arm.Aes.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.Aes");
#endif

    public bool IsArmCrc32Supported =>
#if NET6_0_OR_GREATER
        Crc32.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.Crc32");
#endif

    public bool IsArmDpSupported =>
#if NET6_0_OR_GREATER
        Dp.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.Dp");
#endif

    public bool IsArmRdmSupported =>
#if NET6_0_OR_GREATER
        Rdm.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.Rdm");
#endif

    public bool IsArmSha1Supported =>
#if NET6_0_OR_GREATER
        Sha1.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.Sha1");
#endif

    public bool IsArmSha256Supported =>
#if NET6_0_OR_GREATER
        Sha256.IsSupported;
#else
        GetIsSupported("System.Runtime.Intrinsics.Arm.Sha256");
#endif

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
