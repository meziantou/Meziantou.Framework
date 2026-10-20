using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A feature of the OpenType Layout tables to apply to a run, with its value: 0 turns it off, 1 turns it on, a larger
/// value selects an alternate. A feature applies to the whole run, or to the characters of a range.
/// </summary>
/// <param name="Tag">The tag of the feature.</param>
/// <param name="Value">The value of the feature.</param>
/// <param name="Start">The cluster of the first character the value is for.</param>
/// <param name="End">The cluster after the last character the value is for.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ShapingFeature(uint Tag, int Value, int Start = 0, int End = int.MaxValue)
{
    /// <summary>Initializes a feature for the whole run from the four characters of its tag.</summary>
    /// <param name="tag">The four characters of the tag, such as <c>liga</c>.</param>
    /// <param name="value">The value of the feature.</param>
    public ShapingFeature(string tag, int value = 1)
        : this(ToTag(tag), value)
    {
    }

    /// <summary>Gets a value indicating whether the value is for the whole run.</summary>
    public bool IsGlobal => Start == 0 && End == int.MaxValue;

    /// <summary>Converts the four characters of a tag to its value.</summary>
    public static uint ToTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length != 4)
            throw new ArgumentException("A tag has four characters.", nameof(tag));

        return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
    }
}
