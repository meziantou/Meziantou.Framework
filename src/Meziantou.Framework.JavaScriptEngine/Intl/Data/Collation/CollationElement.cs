using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

/// <summary>
/// A collation element of the CLDR root collation (allkeys_CLDR.txt, in the format of the DUCET): a 16-bit primary
/// weight, a 9-bit secondary weight, a 5-bit tertiary weight and whether it is variable ('*' in allkeys).
/// <code>bits 31-16: primary, 15-7: secondary, 6-2: tertiary, 0: variable</code>
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CollationElement(uint Value)
{
    public static CollationElement Create(int primary, int secondary, int tertiary, bool isVariable)
    {
        if ((uint)primary > 0xFFFF || (uint)secondary > 0x1FF || (uint)tertiary > 0x1F)
            throw new ArgumentOutOfRangeException(nameof(primary), "A collation weight is out of range");

        return new CollationElement(((uint)primary << 16) | ((uint)secondary << 7) | ((uint)tertiary << 2) | (isVariable ? 1u : 0u));
    }

    public int Primary => (int)(Value >> 16);

    public int Secondary => (int)((Value >> 7) & 0x1FF);

    public int Tertiary => (int)((Value >> 2) & 0x1F);

    public bool IsVariable => (Value & 1) != 0;

    /// <summary>Gets whether all the weights are zero (a completely ignorable element).</summary>
    public bool IsIgnorable => (Value & ~1u) == 0;

    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[{(IsVariable ? '*' : '.')}{Primary:X4}.{Secondary:X4}.{Tertiary:X4}]");
}
