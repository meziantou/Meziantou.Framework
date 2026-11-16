namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The IDs of the coders, as big-endian numbers.</summary>
internal static class SevenZipMethodId
{
    public const ulong Copy = 0x00;
    public const ulong Delta = 0x03;
    public const ulong X86 = 0x03030103;
    public const ulong PowerPC = 0x03030205;
    public const ulong IA64 = 0x03030401;
    public const ulong Arm = 0x03030501;
    public const ulong ArmThumb = 0x03030701;
    public const ulong Sparc = 0x03030805;
    public const ulong Arm64 = 0x0A;
    public const ulong RiscV = 0x0B;
    public const ulong Lzma = 0x030101;
    public const ulong Lzma2 = 0x21;
    public const ulong Ppmd = 0x030401;
    public const ulong BZip2 = 0x040202;
    public const ulong Deflate = 0x040108;
    public const ulong Deflate64 = 0x040109;
    public const ulong Bcj2 = 0x0303011B;
    public const ulong Aes = 0x06F10701;

    public static string Format(ulong id) => "0x" + id.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);

    public static string GetName(ulong id) => id switch
    {
        Copy => "Copy",
        Delta => "Delta",
        X86 => "BCJ",
        PowerPC => "PPC",
        IA64 => "IA64",
        Arm => "ARM",
        ArmThumb => "ARMT",
        Sparc => "SPARC",
        Arm64 => "ARM64",
        RiscV => "RISCV",
        Lzma => "LZMA",
        Lzma2 => "LZMA2",
        Ppmd => "PPMd",
        BZip2 => "BZip2",
        Deflate => "Deflate",
        Deflate64 => "Deflate64",
        Bcj2 => "BCJ2",
        Aes => "7zAES",
        _ => Format(id),
    };
}
