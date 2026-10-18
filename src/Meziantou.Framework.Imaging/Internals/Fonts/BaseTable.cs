using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The baseline table: OpenType specification, "BASE - Baseline Table". For each script, it gives where the baselines
/// of the font are (the alphabetic one, the hanging one, the bottom and the top of the ideographic em box), along the
/// y axis of the glyphs for horizontal lines and along their x axis for vertical lines. Text of several scripts and
/// sizes is aligned with them.
/// </summary>
/// <remarks>
/// <para>
/// The header is validated when the face is created. The rest is read when a baseline is asked for: a malformed
/// offset or value is an <see cref="InvalidFontException"/>.
/// </para>
/// <para>
/// The coordinate of a baseline whose position depends on a point of a glyph (the second format) is the value it
/// gives: points only move with hinting, which the library does not do. The device tables that adjust a value to a
/// size are not used either, for the same reason; the variation indexes of a variable font are.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BaseTable
{
    public static readonly uint Roman = ShapingFeature.ToTag("romn");
    public static readonly uint Hanging = ShapingFeature.ToTag("hang");
    public static readonly uint IdeographicBottom = ShapingFeature.ToTag("ideo");
    public static readonly uint IdeographicTop = ShapingFeature.ToTag("idtp");

    private const int VariationIndexFormat = 0x8000;

    private BaseTable(int horizontalAxis, int verticalAxis, int variationStore)
    {
        HorizontalAxis = horizontalAxis;
        VerticalAxis = verticalAxis;
        VariationStore = variationStore;
    }

    /// <summary>Gets the position of the axis of the baselines of horizontal lines in the table; 0 when the table has none.</summary>
    public int HorizontalAxis { get; }

    /// <summary>Gets the position of the axis of the baselines of vertical lines in the table; 0 when the table has none.</summary>
    public int VerticalAxis { get; }

    /// <summary>Gets the position of the item variation store of the table (version 1.1); 0 when the table has none.</summary>
    public int VariationStore { get; }

    /// <summary>Reads the header of the table.</summary>
    /// <returns><see langword="false"/> when the version of the table is not known, or its header is not inside it: the font is then used without it.</returns>
    public static bool TryRead(ReadOnlySpan<byte> table, out BaseTable result)
    {
        result = default;
        if (table.Length < 8 || FontBinary.UInt16(table, 0) != 1)
            return false;

        var horizontal = FontBinary.UInt16(table, 4);
        var vertical = FontBinary.UInt16(table, 6);
        var store = 0;
        if (FontBinary.UInt16(table, 2) >= 1 && table.Length >= 12)
        {
            // A store that is not readable is left out: the baselines then do not vary.
            var offset = FontBinary.UInt32(table, 8);
            store = offset != 0 && offset <= table.Length - 8 && FontBinary.UInt16(table, (int)offset) == 1 ? (int)offset : 0;
        }

        // An axis is two offsets
        if (horizontal > table.Length - 4 || vertical > table.Length - 4)
            return false;

        result = new BaseTable(horizontal, vertical, store);
        return true;
    }

    /// <summary>Finds a baseline of a script.</summary>
    /// <param name="table">The table.</param>
    /// <param name="isVertical"><see langword="true"/> for the baselines of vertical lines, along the x axis of the glyphs.</param>
    /// <param name="scripts">The script tags to look for, by order of preference.</param>
    /// <param name="baseline">The tag of the baseline.</param>
    /// <param name="coordinate">The coordinate of the baseline in font units, before the variations of an instance.</param>
    /// <param name="outer">The outer index of the delta of the coordinate in the item variation store, or -1 when it does not vary.</param>
    /// <param name="inner">The inner index of the delta.</param>
    /// <returns><see langword="false"/> when the table does not give the baseline for the first of the scripts it has.</returns>
    public bool TryGetBaseline(ReadOnlySpan<byte> table, bool isVertical, ReadOnlySpan<uint> scripts, uint baseline, out int coordinate, out int outer, out int inner)
    {
        coordinate = 0;
        outer = -1;
        inner = 0;
        var axis = isVertical ? VerticalAxis : HorizontalAxis;
        if (axis == 0)
            return false;

        // The baselines of the axis, sorted by tag: the values of each script are in their order.
        var tagList = FontBinary.UInt16(table, axis);
        var scriptList = FontBinary.UInt16(table, axis + 2);
        if (tagList == 0 || scriptList == 0)
            return false;

        tagList += axis;
        scriptList += axis;
        var index = FindTag(table, tagList, baseline);
        if (index < 0)
            return false;

        var script = FindScript(table, scriptList, scripts);
        if (script < 0)
            return false;

        var values = FontBinary.UInt16(table, script);
        if (values == 0)
            return false;

        values += script;
        if (index >= FontBinary.UInt16(table, values + 2))
            return false;

        var offset = FontBinary.UInt16(table, values + 4 + (index * 2));
        if (offset == 0)
            return false;

        var coord = values + offset;
        var format = FontBinary.UInt16(table, coord);
        if (format is < 1 or > 3)
            return false;

        coordinate = FontBinary.Int16(table, coord + 2);
        if (format == 3 && FontBinary.UInt16(table, coord + 4) is not 0 and var device)
        {
            device += coord;
            if (FontBinary.UInt16(table, device + 4) == VariationIndexFormat)
            {
                outer = FontBinary.UInt16(table, device);
                inner = FontBinary.UInt16(table, device + 2);
            }
        }

        return true;
    }

    /// <summary>Finds the index of a baseline in the tag list of an axis, or -1.</summary>
    private static int FindTag(ReadOnlySpan<byte> table, int tagList, uint tag)
    {
        var count = FontBinary.UInt16(table, tagList);
        FontBinary.EnsureRange(table, tagList + 2, (long)count * 4);
        for (var i = 0; i < count; i++)
        {
            if (FontBinary.UInt32(table, tagList + 2 + (i * 4)) == tag)
                return i;
        }

        return -1;
    }

    /// <summary>Finds the position of the first of some scripts the script list of an axis has, or -1.</summary>
    private static int FindScript(ReadOnlySpan<byte> table, int scriptList, ReadOnlySpan<uint> scripts)
    {
        var count = FontBinary.UInt16(table, scriptList);
        FontBinary.EnsureRange(table, scriptList + 2, (long)count * 6);
        foreach (var script in scripts)
        {
            for (var i = 0; i < count; i++)
            {
                var record = scriptList + 2 + (i * 6);
                if (FontBinary.UInt32(table, record) == script)
                {
                    var offset = FontBinary.UInt16(table, record + 4);
                    return offset == 0 ? -1 : scriptList + offset;
                }
            }
        }

        return -1;
    }
}
