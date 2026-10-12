namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the <c>fvar</c> table: the axes a variable font varies along, and its named instances.</summary>
/// <remarks>OpenType specification, "fvar - Font Variations Table".</remarks>
internal sealed class FvarTable
{
    private const int AxisRecordLength = 20;

    private FvarTable(FontAxisRecord[] axes, FontInstanceRecord[] instances)
    {
        Axes = axes;
        Instances = instances;
    }

    /// <summary>Gets the axes, in the order every other variation table refers to them.</summary>
    public FontAxisRecord[] Axes { get; }

    /// <summary>Gets the named instances.</summary>
    public FontInstanceRecord[] Instances { get; }

    /// <summary>Reads the table, or returns <see langword="null"/> when it declares no axis or a version that is not known.</summary>
    public static FvarTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < 16 || FontBinary.UInt16(table, 0) != 1)
            return null;

        var axesOffset = FontBinary.UInt16(table, 4);
        var axisCount = FontBinary.UInt16(table, 8);
        var axisSize = FontBinary.UInt16(table, 10);
        var instanceCount = FontBinary.UInt16(table, 12);
        var instanceSize = FontBinary.UInt16(table, 14);
        if (axisCount == 0)
            return null;

        if (axisSize < AxisRecordLength)
            throw FontErrors.InvalidTable(FontTags.Fvar, "has axis records shorter than their fields.");

        FontBinary.EnsureRange(table, axesOffset, (long)axisCount * axisSize);
        var axes = new FontAxisRecord[axisCount];
        for (var i = 0; i < axes.Length; i++)
        {
            var record = table.Slice(axesOffset + (i * axisSize), AxisRecordLength);
            if (record[..4].ContainsAnyExceptInRange((byte)' ', (byte)'~'))
                throw FontErrors.InvalidTable(FontTags.Fvar, "has an axis whose tag is not four printable characters.");

            var minimum = FontBinary.Int32(record, 4);
            var @default = FontBinary.Int32(record, 8);
            var maximum = FontBinary.Int32(record, 12);

            // The default is within the range of the axis; a range that does not contain it is widened.
            axes[i] = new FontAxisRecord(
                FontBinary.UInt32(record, 0),
                Math.Min(minimum, @default),
                @default,
                Math.Max(maximum, @default),
                isHidden: (FontBinary.UInt16(record, 16) & 0x0001) != 0,
                FontBinary.UInt16(record, 18));
        }

        // An instance: the identifier of its name, flags, a coordinate per axis, and optionally the identifier of its
        // PostScript name.
        var coordinatesLength = 4 + (axisCount * 4);
        if (instanceCount != 0 && instanceSize < coordinatesLength)
            throw FontErrors.InvalidTable(FontTags.Fvar, "has instance records shorter than a coordinate per axis.");

        var instancesOffset = axesOffset + ((long)axisCount * axisSize);
        FontBinary.EnsureRange(table, instancesOffset, (long)instanceCount * instanceSize);
        var instances = new FontInstanceRecord[instanceCount];
        for (var i = 0; i < instances.Length; i++)
        {
            var record = table.Slice((int)instancesOffset + (i * instanceSize), instanceSize);
            var coordinates = new int[axisCount];
            for (var axis = 0; axis < coordinates.Length; axis++)
            {
                coordinates[axis] = FontBinary.Int32(record, 4 + (axis * 4));
            }

            var postScriptNameId = instanceSize >= coordinatesLength + 2 ? FontBinary.UInt16(record, coordinatesLength) : 0xFFFF;
            instances[i] = new FontInstanceRecord(FontBinary.UInt16(record, 0), postScriptNameId, coordinates);
        }

        return new FvarTable(axes, instances);
    }
}
