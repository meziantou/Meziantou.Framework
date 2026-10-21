namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The format of the collation elements of the collator (64 bits, as the CEs of ICU):
/// <code>bits 63-32: primary, 31-16: secondary, 15-14: case (0 lower, 1 mixed, 2 upper), 13-0: tertiary</code>
/// The weights of the root collation (allkeys_CLDR.txt: 16-bit primaries, 9-bit secondaries, 5-bit tertiaries) are
/// spread over the larger weights, so that a tailoring can insert weights between them: a root primary <c>p</c> is
/// <c>p &lt;&lt; 16</c>; an implicit weight (the pair <c>[AAAA.0020.0002][BBBB.0000.0000]</c> of UCA) is the single
/// primary <c>AAAA &lt;&lt; 16 | BBBB</c>.
/// </summary>
internal static class CollationWeights
{
    public const uint CommonWeight16 = 0x0500;

    /// <summary>The secondary or tertiary weight of the nodes inserted for <c>[before 2]</c> and <c>[before 3]</c>, below the common weight.</summary>
    public const uint BeforeWeight16 = 0x0100;

    /// <summary>The upper limit of the secondary weights.</summary>
    public const uint SecondaryBoundary = 0xFF00;

    /// <summary>The upper limit of the tertiary weights (14 bits).</summary>
    public const uint TertiaryBoundary = 0x3F00;

    /// <summary>The upper limit of the primary weights (above the last root primary).</summary>
    public const uint PrimaryBoundary = 0xFFFF0000;

    public const ulong CaseMask = 0xC000;
    public const ulong UpperCase = 0x8000;
    public const ulong MixedCase = 0x4000;
    public const uint TertiaryMask = 0x3FFF;

    /// <summary>The CE of the common secondary and tertiary weights of a primary.</summary>
    public const ulong CommonSecondaryAndTertiary = ((ulong)CommonWeight16 << 16) | CommonWeight16;

    public static uint Primary(ulong ce) => (uint)(ce >> 32);

    public static uint Secondary(ulong ce) => (uint)(ce >> 16) & 0xFFFF;

    public static uint Tertiary(ulong ce) => (uint)ce & TertiaryMask;

    public static int Case(ulong ce) => (int)((ce >> 14) & 3);

    public static ulong MakeCE(uint primary, uint secondary, uint tertiaryWithCase) => ((ulong)primary << 32) | (secondary << 16) | tertiaryWithCase;

    /// <summary>The CE of a primary with the common secondary and tertiary weights (lowercase).</summary>
    public static ulong MakeCE(uint primary) => ((ulong)primary << 32) | CommonSecondaryAndTertiary;

    /// <summary>
    /// The secondary weight of a root secondary weight (0x20 is the common weight, the secondaries of the combining marks
    /// are above): the tailored secondary weights after the common weight are below the ones of the combining marks.
    /// </summary>
    public static uint ScaleSecondary(int secondary) => secondary switch
    {
        0 => 0,
        0x20 => CommonWeight16,
        < 0x20 => CommonWeight16 - (uint)((0x20 - secondary) * 0x20),
        _ => 0x2000 + ((uint)(secondary - 0x21) * 0x60),
    };

    /// <summary>The tertiary weight of a root tertiary weight (0x02 is the common weight).</summary>
    public static uint ScaleTertiary(int tertiary) => tertiary switch
    {
        0 => 0,
        1 => 0x0300,
        2 => CommonWeight16,
        _ => 0x0600 + ((uint)(tertiary - 3) * 0x100),
    };

    /// <summary>Gets whether a root tertiary weight is the one of an uppercase character (UCA tertiary weight table).</summary>
    public static bool IsUppercaseTertiary(int tertiary) => tertiary is (>= 0x08 and <= 0x0C) or 0x1D;
}
