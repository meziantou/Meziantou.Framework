using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>A variation axis of a variable font (https://learn.microsoft.com/typography/opentype/spec/fvar#variationaxisrecord).</summary>
/// <param name="Tag">The tag of the axis (e.g. <c>wght</c>).</param>
/// <param name="MinValue">The minimum value of the axis, in user coordinates.</param>
/// <param name="DefaultValue">The default value of the axis.</param>
/// <param name="MaxValue">The maximum value of the axis.</param>
/// <param name="IsHidden">Whether the axis should not be exposed to the users (<c>HIDDEN_AXIS</c> flag).</param>
/// <param name="NameId">The name identifier of the name of the axis.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontVariationAxis(uint Tag, double MinValue, double DefaultValue, double MaxValue, bool IsHidden, ushort NameId);

/// <summary>A named instance of a variable font (https://learn.microsoft.com/typography/opentype/spec/fvar#instancerecord).</summary>
/// <param name="SubfamilyNameId">The name identifier of the subfamily name of the instance (e.g. "Bold").</param>
/// <param name="Coordinates">The user coordinates of the instance, one per axis.</param>
internal sealed record FontNamedInstance(ushort SubfamilyNameId, double[] Coordinates);

/// <summary>A value of a variation axis, in user coordinates (e.g. <c>font-variation-settings: "wght" 650</c>).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontVariationSetting(uint Tag, double Value);

/// <summary>
/// An instance of a variable font: the normalized coordinates of its axes (https://learn.microsoft.com/typography/opentype/spec/otvaroverview#coordinate-scales-and-normalization),
/// in the order of the axes of the fvar table. The instance of a font is created by <see cref="FontVariations.CreateInstance"/>.
/// </summary>
internal sealed class FontVariationInstance
{
    internal FontVariationInstance(double[] coordinates)
    {
        NormalizedCoordinates = coordinates;
        IsDefault = Array.TrueForAll(coordinates, value => value == 0);
    }

    /// <summary>The normalized coordinates, from -1 to 1, after the avar mapping.</summary>
    public IReadOnlyList<double> NormalizedCoordinates { get; }

    /// <summary>Whether the instance is the default instance of the font (all the normalized coordinates are 0).</summary>
    public bool IsDefault { get; }

    internal double GetCoordinate(int axis) => axis < NormalizedCoordinates.Count ? NormalizedCoordinates[axis] : 0;
}

/// <summary>
/// The variation tables of a font: the axes and the named instances (fvar), the axis variations (avar), the advance
/// variations (HVAR) and the metrics variations (MVAR) (https://learn.microsoft.com/typography/opentype/spec/otvaroverview).
/// </summary>
/// <remarks>
/// The advances of a variable font without HVAR table (whose advance variations are only in the phantom points of gvar)
/// are the advances of the default instance.
/// </remarks>
internal sealed class FontVariations
{
    private readonly OpenTypeFont _font;
    private readonly FontVariationAxis[] _axes;
    private readonly FontNamedInstance[] _instances;

    /// <summary>The tags of the values of the MVAR table (https://learn.microsoft.com/typography/opentype/spec/mvar#value-tags).</summary>
    internal static class MetricsTag
    {
        public const uint HorizontalAscender = 0x68617363; // hasc
        public const uint HorizontalDescender = 0x68647363; // hdsc
        public const uint HorizontalLineGap = 0x686C6770; // hlgp
        public const uint HorizontalClippingAscent = 0x68636C61; // hcla
        public const uint HorizontalClippingDescent = 0x68636C64; // hcld
        public const uint XHeight = 0x78686774; // xhgt
        public const uint CapHeight = 0x63706874; // cpht
        public const uint UnderlineSize = 0x756E6473; // unds
        public const uint UnderlineOffset = 0x756E646F; // undo
        public const uint StrikeoutSize = 0x73747273; // strs
        public const uint StrikeoutOffset = 0x7374726F; // stro
        public const uint SubscriptYOffset = 0x7362796F; // sbyo
        public const uint SuperscriptYOffset = 0x7370796F; // spyo
    }

    internal FontVariations(OpenTypeFont font)
    {
        _font = font;

        // https://learn.microsoft.com/typography/opentype/spec/fvar
        var fvar = font.GetTable(OpenTypeTag.Fvar);
        var axesOffset = FontBinary.U16(fvar, 4);
        var axisCount = FontBinary.U16(fvar, 8);
        var axisSize = FontBinary.U16(fvar, 10);
        var instanceCount = FontBinary.U16(fvar, 12);
        var instanceSize = FontBinary.U16(fvar, 14);
        if (fvar.Length < 16 || FontBinary.U16(fvar, 0) != 1 || axisSize < 20 || !FontBinary.IsInRange(fvar, axesOffset, (long)axisCount * axisSize))
        {
            _axes = [];
            _instances = [];
            return;
        }

        _axes = new FontVariationAxis[axisCount];
        for (var i = 0; i < axisCount; i++)
        {
            var axis = axesOffset + (i * axisSize);
            var min = FontBinary.Fixed(fvar, axis + 4);
            var value = FontBinary.Fixed(fvar, axis + 8);
            var max = FontBinary.Fixed(fvar, axis + 12);

            // An axis whose values are not ordered is ignored (it keeps its default value)
            if (min > value || value > max)
            {
                min = max = value;
            }

            _axes[i] = new FontVariationAxis(FontBinary.U32(fvar, axis), min, value, max, (FontBinary.U16(fvar, axis + 16) & 1) != 0, FontBinary.U16(fvar, axis + 18));
        }

        // The instances follow the axes; an instance has a PostScript name identifier when its size is 4 bytes larger
        var instancesOffset = axesOffset + (axisCount * axisSize);
        if (instanceSize < (axisCount * 4) + 4 || !FontBinary.IsInRange(fvar, instancesOffset, (long)instanceCount * instanceSize))
        {
            _instances = [];
            return;
        }

        _instances = new FontNamedInstance[instanceCount];
        for (var i = 0; i < instanceCount; i++)
        {
            var instance = instancesOffset + (i * instanceSize);
            var coordinates = new double[axisCount];
            for (var axis = 0; axis < axisCount; axis++)
            {
                coordinates[axis] = FontBinary.Fixed(fvar, instance + 4 + (axis * 4));
            }

            _instances[i] = new FontNamedInstance(FontBinary.U16(fvar, instance), coordinates);
        }
    }

    /// <summary>Whether the font is a variable font (it has variation axes).</summary>
    public bool IsVariable => _axes.Length > 0;

    /// <summary>The variation axes of the font, in the order of the fvar table.</summary>
    public IReadOnlyList<FontVariationAxis> Axes => _axes;

    /// <summary>The named instances of the font.</summary>
    public IReadOnlyList<FontNamedInstance> NamedInstances => _instances;

    /// <summary>Returns the axis with the tag, or null when the font does not have it.</summary>
    public FontVariationAxis? GetAxis(uint tag)
    {
        foreach (var axis in _axes)
        {
            if (axis.Tag == tag)
                return axis;
        }

        return null;
    }

    /// <summary>
    /// Creates the instance of the font for the values of the axes (the axes without value have their default value, and
    /// the last value of an axis wins). The values are clamped to the range of their axis, normalized and mapped by the
    /// avar table (https://learn.microsoft.com/typography/opentype/spec/otvaroverview#coordinate-scales-and-normalization).
    /// </summary>
    public FontVariationInstance CreateInstance(ReadOnlySpan<FontVariationSetting> settings)
    {
        var coordinates = new double[_axes.Length];
        for (var i = 0; i < _axes.Length; i++)
        {
            var axis = _axes[i];
            var value = axis.DefaultValue;
            foreach (var setting in settings)
            {
                if (setting.Tag == axis.Tag && !double.IsNaN(setting.Value))
                {
                    value = setting.Value;
                }
            }

            value = Math.Clamp(value, axis.MinValue, axis.MaxValue);
            double normalized;
            if (value < axis.DefaultValue)
            {
                normalized = (value - axis.DefaultValue) / (axis.DefaultValue - axis.MinValue);
            }
            else if (value > axis.DefaultValue)
            {
                normalized = (value - axis.DefaultValue) / (axis.MaxValue - axis.DefaultValue);
            }
            else
            {
                normalized = 0;
            }

            coordinates[i] = MapAvar(i, ToF2Dot14(normalized));
        }

        ApplyAvar2(coordinates);
        return new FontVariationInstance(coordinates);
    }

    /// <summary>
    /// Applies the axis variations of the version 2 of avar (https://learn.microsoft.com/typography/opentype/spec/avar#axis-value-mapping-algorithm):
    /// the delta of each axis is the delta of its item (from the axis index map, else the item of the axis index) in the
    /// variation store, evaluated at the coordinates mapped by the segment maps; it is rounded to <c>F2DOT14</c> units and
    /// added to the coordinate, which is clamped to [-1, 1] (as HarfBuzz).
    /// </summary>
    private void ApplyAvar2(double[] coordinates)
    {
        var avar = _font.GetTable(OpenTypeTag.Avar);
        if (avar.Length < 8 || FontBinary.U16(avar, 0) != 2 || FontBinary.U16(avar, 6) != _axes.Length)
            return;

        // The offsets of the axis index map and of the variation store follow the segment maps of the axes
        var offset = 8;
        for (var i = 0; i < _axes.Length; i++)
        {
            if (!FontBinary.IsInRange(avar, offset, 2))
                return;

            offset += 2 + (FontBinary.U16(avar, offset) * 4);
        }

        if (!FontBinary.IsInRange(avar, offset, 8))
            return;

        var mapOffset = FontBinary.U32(avar, offset);
        var storeOffset = FontBinary.U32(avar, offset + 4);
        if (storeOffset == 0)
            return;

        var store = FontBinary.Slice(avar, storeOffset);
        var map = mapOffset == 0 ? default : FontBinary.Slice(avar, mapOffset);
        var mapped = new FontVariationInstance((double[])coordinates.Clone());
        for (var i = 0; i < coordinates.Length; i++)
        {
            var (outer, inner) = map.IsEmpty ? (0, i) : MapDeltaSetIndex(map, i);
            var delta = Math.Round(GetDelta(store, outer, inner, mapped), MidpointRounding.AwayFromZero);
            if (delta != 0)
            {
                coordinates[i] = Math.Clamp(Math.Round(coordinates[i] * 16384, MidpointRounding.AwayFromZero) + delta, -16384, 16384) / 16384;
            }
        }
    }

    /// <summary>The value rounded to the nearest <c>F2DOT14</c> value, as required by the normalization.</summary>
    private static double ToF2Dot14(double value) => Math.Round(Math.Clamp(value, -1, 1) * 16384, MidpointRounding.AwayFromZero) / 16384;

    /// <summary>Maps a normalized coordinate with the segment map of its axis (https://learn.microsoft.com/typography/opentype/spec/avar).</summary>
    private double MapAvar(int axisIndex, double value)
    {
        var avar = _font.GetTable(OpenTypeTag.Avar);
        if (avar.Length < 8 || FontBinary.U16(avar, 0) is not (1 or 2) || FontBinary.U16(avar, 6) != _axes.Length)
            return value;

        // The segment maps of the axes follow each other
        var offset = 8;
        for (var i = 0; i < axisIndex; i++)
        {
            offset += 2 + (FontBinary.U16(avar, offset) * 4);
        }

        var count = FontBinary.U16(avar, offset);
        if (count == 0 || !FontBinary.IsInRange(avar, offset + 2, count * 4))
            return value;

        for (var i = 1; i < count; i++)
        {
            var from = FontBinary.F2Dot14(avar, offset + 2 + (i * 4));
            if (value < from)
            {
                var previousFrom = FontBinary.F2Dot14(avar, offset + 2 + ((i - 1) * 4));
                var previousTo = FontBinary.F2Dot14(avar, offset + 2 + ((i - 1) * 4) + 2);
                var to = FontBinary.F2Dot14(avar, offset + 2 + (i * 4) + 2);
                if (from == previousFrom)
                    return previousTo;

                return ToF2Dot14(previousTo + ((value - previousFrom) * (to - previousTo) / (from - previousFrom)));
            }
        }

        // The value is after the last map: it is mapped to the last value when it is equal to the last coordinate
        var last = offset + 2 + ((count - 1) * 4);
        return value == FontBinary.F2Dot14(avar, last) ? FontBinary.F2Dot14(avar, last + 2) : value;
    }

    /// <summary>The delta of the advance width of a glyph in an instance (https://learn.microsoft.com/typography/opentype/spec/hvar).</summary>
    public double GetAdvanceWidthDelta(ushort glyph, FontVariationInstance instance)
    {
        var hvar = _font.GetTable(OpenTypeTag.Hvar);
        if (hvar.Length < 20 || instance.IsDefault)
            return 0;

        var store = FontBinary.Slice(hvar, FontBinary.U32(hvar, 4));
        var mapping = FontBinary.U32(hvar, 8);
        var (outer, inner) = mapping == 0 ? (0, (int)glyph) : MapDeltaSetIndex(FontBinary.Slice(hvar, mapping), glyph);
        return GetDelta(store, outer, inner, instance);
    }

    /// <summary>The delta of a metric of the MVAR table in an instance (https://learn.microsoft.com/typography/opentype/spec/mvar).</summary>
    public double GetMetricsDelta(uint tag, FontVariationInstance instance)
    {
        var mvar = _font.GetTable(OpenTypeTag.Mvar);
        if (mvar.Length < 12 || instance.IsDefault)
            return 0;

        var recordSize = FontBinary.U16(mvar, 6);
        var recordCount = FontBinary.U16(mvar, 8);
        var store = FontBinary.Slice(mvar, FontBinary.U16(mvar, 10));
        if (recordSize < 8)
            return 0;

        // The records are sorted by tag
        int low = 0, high = recordCount - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = 12 + (middle * recordSize);
            var recordTag = FontBinary.U32(mvar, record);
            if (recordTag < tag)
            {
                low = middle + 1;
            }
            else if (recordTag > tag)
            {
                high = middle - 1;
            }
            else
            {
                return GetDelta(store, FontBinary.U16(mvar, record + 4), FontBinary.U16(mvar, record + 6), instance);
            }
        }

        return 0;
    }

    /// <summary>The outer and inner indexes of the delta set of an item (https://learn.microsoft.com/typography/opentype/spec/otvarcommonformats#associating-target-items-to-variation-data).</summary>
    internal static (int Outer, int Inner) MapDeltaSetIndex(ReadOnlySpan<byte> map, int item)
    {
        var format = FontBinary.U8(map, 0);
        var entryFormat = FontBinary.U8(map, 1);
        var count = format == 0 ? FontBinary.U16(map, 2) : FontBinary.U32(map, 2);
        var dataOffset = format == 0 ? 4 : 6;
        if (count == 0)
            return (0, item);

        // The items after the last entry use the last entry
        var index = Math.Min((uint)item, count - 1);
        var entrySize = ((entryFormat & 0x30) >> 4) + 1;
        var innerBitCount = (entryFormat & 0x0F) + 1;
        var position = dataOffset + (index * entrySize);
        if (position > int.MaxValue)
            return (0, 0);

        var entry = 0u;
        for (var i = 0; i < entrySize; i++)
        {
            entry = (entry << 8) | FontBinary.U8(map, (int)position + i);
        }

        return ((int)(entry >> innerBitCount), (int)(entry & ((1u << innerBitCount) - 1)));
    }

    /// <summary>
    /// The delta of an item of an item variation store in an instance: the sum of the deltas of the regions weighted by the
    /// scalars of the regions (https://learn.microsoft.com/typography/opentype/spec/otvarcommonformats#item-variation-store).
    /// </summary>
    internal static double GetDelta(ReadOnlySpan<byte> store, int outer, int inner, FontVariationInstance instance)
    {
        if (store.Length < 8 || FontBinary.U16(store, 0) != 1 || outer >= FontBinary.U16(store, 6))
            return 0;

        var regions = FontBinary.Slice(store, FontBinary.U32(store, 2));
        var data = FontBinary.Slice(store, FontBinary.U32(store, 8 + (outer * 4)));
        var itemCount = FontBinary.U16(data, 0);
        var wordDeltaCount = FontBinary.U16(data, 2);
        var regionIndexCount = FontBinary.U16(data, 4);
        if (inner >= itemCount)
            return 0;

        // The deltas of an item: wordCount deltas of 2 bytes (4 with LONG_WORDS), then deltas of 1 byte (2 with LONG_WORDS)
        var longWords = (wordDeltaCount & 0x8000) != 0;
        var wordCount = Math.Min(wordDeltaCount & 0x7FFF, (int)regionIndexCount);
        var rowSize = longWords ? (wordCount * 4) + ((regionIndexCount - wordCount) * 2) : (wordCount * 2) + (regionIndexCount - wordCount);
        var row = 6 + (regionIndexCount * 2) + (inner * rowSize);
        if (!FontBinary.IsInRange(data, row, rowSize))
            return 0;

        var regionAxisCount = FontBinary.U16(regions, 0);
        var regionCount = FontBinary.U16(regions, 2);
        var result = 0.0;
        var position = row;
        for (var i = 0; i < regionIndexCount; i++)
        {
            int delta;
            if (i < wordCount)
            {
                delta = longWords ? FontBinary.I32(data, position) : FontBinary.I16(data, position);
                position += longWords ? 4 : 2;
            }
            else
            {
                delta = longWords ? FontBinary.I16(data, position) : FontBinary.I8(data, position);
                position += longWords ? 2 : 1;
            }

            var region = FontBinary.U16(data, 6 + (i * 2));
            if (delta == 0 || region >= regionCount)
                continue;

            result += delta * GetRegionScalar(regions, region, regionAxisCount, instance);
        }

        return result;
    }

    /// <summary>The scalar of a region for an instance (https://learn.microsoft.com/typography/opentype/spec/otvaroverview#algorithm-for-interpolation-of-instance-values).</summary>
    private static double GetRegionScalar(ReadOnlySpan<byte> regions, int region, int axisCount, FontVariationInstance instance)
    {
        var scalar = 1.0;
        var offset = 4 + (region * axisCount * 6);
        for (var axis = 0; axis < axisCount; axis++)
        {
            var start = FontBinary.F2Dot14(regions, offset + (axis * 6));
            var peak = FontBinary.F2Dot14(regions, offset + (axis * 6) + 2);
            var end = FontBinary.F2Dot14(regions, offset + (axis * 6) + 4);

            // The axes whose peak is 0, and the invalid ranges, do not restrict the region
            if (peak == 0 || start > peak || peak > end || (start < 0 && end > 0))
                continue;

            var coordinate = instance.GetCoordinate(axis);
            if (coordinate == peak)
                continue;

            if (coordinate <= start || coordinate >= end)
                return 0;

            scalar *= coordinate < peak ? (coordinate - start) / (peak - start) : (end - coordinate) / (end - peak);
        }

        return scalar;
    }
}
