namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>The architecture of a BCJ filter.</summary>
internal enum BranchFilterKind
{
    /// <summary>x86 and x86-64 (E8/E9 call and jump).</summary>
    X86,

    /// <summary>Big-endian PowerPC.</summary>
    PowerPC,

    /// <summary>Itanium (IA-64).</summary>
    IA64,

    /// <summary>Little-endian ARM.</summary>
    Arm,

    /// <summary>Little-endian ARM-Thumb.</summary>
    ArmThumb,

    /// <summary>SPARC.</summary>
    Sparc,

    /// <summary>ARM64 (BL and ADRP).</summary>
    Arm64,

    /// <summary>RISC-V (JAL and AUIPC).</summary>
    RiscV,
}
