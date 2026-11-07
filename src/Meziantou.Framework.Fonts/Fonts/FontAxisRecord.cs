using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>An axis of a variable font. The values are 16.16 fixed-point numbers, in the units of the axis.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct FontAxisRecord
{
    internal FontAxisRecord(uint tag, int minimum, int @default, int maximum, bool isHidden, int nameId)
    {
        Tag = tag;
        Minimum = minimum;
        Default = @default;
        Maximum = maximum;
        IsHidden = isHidden;
        NameId = nameId;
    }

    /// <summary>Gets the tag of the axis, such as <c>wght</c>, as a big-endian integer.</summary>
    public uint Tag { get; }

    /// <summary>Gets the smallest value of the axis.</summary>
    public int Minimum { get; }

    /// <summary>Gets the value of the axis in the default instance.</summary>
    public int Default { get; }

    /// <summary>Gets the largest value of the axis.</summary>
    public int Maximum { get; }

    /// <summary>Gets a value indicating whether the font asks that the axis is not shown to the users.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets the identifier of the name of the axis in the <c>name</c> table.</summary>
    public int NameId { get; }
}
