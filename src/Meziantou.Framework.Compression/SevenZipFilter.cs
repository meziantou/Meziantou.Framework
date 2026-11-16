namespace Meziantou.Framework.Compression;

/// <summary>A filter that makes data easier to compress, applied before the compression method.</summary>
public enum SevenZipFilter
{
    /// <summary>The x86 filter for the executables (by their extension: .exe, .dll, .sys, ...), and no filter for the other files.</summary>
    Auto,

    /// <summary>No filter.</summary>
    None,

    /// <summary>The x86 branch converter (BCJ), for x86 and x64 executables.</summary>
    X86,

    /// <summary>The 32-bit ARM branch converter.</summary>
    Arm,

    /// <summary>The ARM64 branch converter.</summary>
    Arm64,

    /// <summary>The ARM Thumb branch converter.</summary>
    ArmThumb,

    /// <summary>The PowerPC (big-endian) branch converter.</summary>
    PowerPC,

    /// <summary>The Itanium branch converter.</summary>
    IA64,

    /// <summary>The SPARC branch converter.</summary>
    Sparc,

    /// <summary>The RISC-V branch converter.</summary>
    RiscV,
}
