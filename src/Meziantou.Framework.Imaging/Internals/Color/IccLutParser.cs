namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Translates the lookup-table tag types of ICC profiles to conversion stages on normalized values in [0, 1]:
/// <c>lut8Type</c> and <c>lut16Type</c> (ICC.1:2022 sections 10.10 and 10.11; ICC.1:2001-04 sections 6.5.7 and 6.5.8),
/// <c>lutAToBType</c> and <c>lutBToAType</c> (ICC.1:2022 sections 10.12 and 10.13).
/// </summary>
/// <remarks>
/// Every size is validated against the tag before a stage is created: the number of channels (at most
/// <see cref="IccPipeline.MaxChannels"/>), the grid points (at least two per input) and the table lengths, computed
/// without overflow. A stage reads the color lookup table in place, so a parsed tag allocates only its one-dimensional
/// tables and curves (at most a few times the size of the tag). Tags may overlap or share data with other tags.
/// </remarks>
internal static class IccLutParser
{
    private const int Lut8HeaderSize = 48;
    private const int Lut16HeaderSize = 52;
    private const int LutAToBHeaderSize = 32;
    private const int ClutHeaderSize = 20;
    private const int MaxTableEntries = 4096;

    /// <summary>Appends the stages of a lookup-table tag.</summary>
    /// <param name="tag">The tag data, starting at the type signature.</param>
    /// <param name="inputs">The number of input channels the tag must have.</param>
    /// <param name="outputs">The number of output channels the tag must have.</param>
    /// <param name="deviceToConnection">Whether the tag converts device values to the connection space (an AToB tag) or the reverse (a BToA tag).</param>
    /// <param name="connectionIsXyz">Whether the connection space is CIEXYZ: the matrix of a <c>lut8Type</c> or <c>lut16Type</c> is only used when its input is CIEXYZ.</param>
    /// <param name="stages">The stages of the conversion.</param>
    /// <returns><see langword="null"/> on success, else the reason the tag is invalid.</returns>
    public static string? Append(ReadOnlyMemory<byte> tag, int inputs, int outputs, bool deviceToConnection, bool connectionIsXyz, IccStageList stages)
    {
        var data = tag.Span;
        if (data.Length < 12)
            return "the tag is shorter than a lookup table header.";

        return IccReader.ReadUInt32(data) switch
        {
            IccReader.TypeLut8 => AppendLut(tag, inputs, outputs, GetInputSpace(deviceToConnection, connectionIsXyz), stages, bytesPerEntry: 1),
            IccReader.TypeLut16 => AppendLut(tag, inputs, outputs, GetInputSpace(deviceToConnection, connectionIsXyz), stages, bytesPerEntry: 2),
            IccReader.TypeLutAToB when deviceToConnection => AppendLutAToB(tag, inputs, outputs, deviceToConnection: true, InputSpace.Device, stages),
            IccReader.TypeLutBToA when !deviceToConnection => AppendLutAToB(tag, inputs, outputs, deviceToConnection: false, GetInputSpace(deviceToConnection, connectionIsXyz), stages),
            IccReader.TypeLutAToB => "a lutAToBType cannot convert from the profile connection space.",
            IccReader.TypeLutBToA => "a lutBToAType cannot convert to the profile connection space.",
            var type => $"the tag type {IccReader.FormatSignature(type)} is not a lookup table type.",
        };
    }

    /// <summary>The color space of the input of a lookup table: it selects the matrix use and the interpolation.</summary>
    private enum InputSpace
    {
        Device,
        Xyz,
        Lab,
    }

    private static InputSpace GetInputSpace(bool deviceToConnection, bool connectionIsXyz)
        => deviceToConnection ? InputSpace.Device : connectionIsXyz ? InputSpace.Xyz : InputSpace.Lab;

    /// <summary>
    /// Determines the interpolation of a color lookup table (see <see cref="IccClutStage"/>): multilinear when the neutral
    /// axis of the input space is not the diagonal of a grid cell, that is for CIELAB and for four device channels.
    /// </summary>
    private static bool IsMultilinear(InputSpace inputSpace, int inputs) => inputSpace == InputSpace.Lab || inputs == 4;

    /// <summary>
    /// Appends a <c>lut8Type</c> or <c>lut16Type</c>: matrix (CIEXYZ input only), one input table per input channel, the
    /// color lookup table, one output table per output channel.
    /// </summary>
    private static string? AppendLut(ReadOnlyMemory<byte> tag, int inputs, int outputs, InputSpace inputSpace, IccStageList stages, int bytesPerEntry)
    {
        var data = tag.Span;
        var headerSize = bytesPerEntry == 1 ? Lut8HeaderSize : Lut16HeaderSize;
        if (data.Length < headerSize)
            return "the tag is shorter than a lookup table header.";

        if (CheckChannels(data[8], data[9], inputs, outputs) is { } channelError)
            return channelError;

        int gridPoints = data[10];
        if (gridPoints < 2)
            return string.Create(CultureInfo.InvariantCulture, $"the color lookup table has {gridPoints} grid points per input; at least 2 are required.");

        int inputEntries = 256;
        int outputEntries = 256;
        if (bytesPerEntry == 2)
        {
            inputEntries = IccReader.ReadUInt16(data[48..]);
            outputEntries = IccReader.ReadUInt16(data[50..]);
            if (inputEntries is < 2 or > MaxTableEntries || outputEntries is < 2 or > MaxTableEntries)
                return string.Create(CultureInfo.InvariantCulture, $"the input and output tables have {inputEntries} and {outputEntries} entries; 2 to {MaxTableEntries} are allowed.");
        }

        // All lengths in bytes, in 64 bits: 255^4 grid points of 4 outputs do not fit in 32 bits
        long clutLength = outputs * bytesPerEntry;
        for (var i = 0; i < inputs; i++)
        {
            clutLength *= gridPoints;
        }

        long inputLength = (long)inputs * inputEntries * bytesPerEntry;
        long outputLength = (long)outputs * outputEntries * bytesPerEntry;
        if (headerSize + inputLength + clutLength + outputLength > data.Length)
            return "the tables of the lookup table exceed the tag.";

        if (inputSpace == InputSpace.Xyz)
        {
            Span<double> matrix = stackalloc double[9];
            for (var i = 0; i < 9; i++)
            {
                matrix[i] = IccReader.ReadS15Fixed16(data[(12 + (4 * i))..]);
            }

            // The matrix is applied to the encoded values (it is linear, so the encoding scale commutes); the input
            // tables then clip the result to their domain [0, 1]
            stages.Add(new IccMatrixStage(matrix));
        }

        var position = headerSize;
        stages.Add(new IccCurvesStage(ReadTables(data.Slice(position, (int)inputLength), inputs, inputEntries, bytesPerEntry)));
        position += (int)inputLength;

        Span<int> grid = stackalloc int[inputs];
        grid.Fill(gridPoints);
        stages.Add(new IccClutStage(tag.Slice(position, (int)clutLength), grid, outputs, bytesPerEntry, IsMultilinear(inputSpace, inputs)));
        position += (int)clutLength;

        stages.Add(new IccCurvesStage(ReadTables(data.Slice(position, (int)outputLength), outputs, outputEntries, bytesPerEntry)));
        return null;
    }

    /// <summary>
    /// Appends a <c>lutAToBType</c> (A curves, color lookup table, M curves, matrix, B curves) or a <c>lutBToAType</c> (the
    /// same elements in the reverse order). Each element is optional: an offset of 0 means it is absent. The A curves are
    /// on the device side, the M curves, the matrix and the B curves on the connection space side.
    /// </summary>
    private static string? AppendLutAToB(ReadOnlyMemory<byte> tag, int inputs, int outputs, bool deviceToConnection, InputSpace inputSpace, IccStageList stages)
    {
        var data = tag.Span;
        if (data.Length < LutAToBHeaderSize)
            return "the tag is shorter than a lookup table header.";

        if (CheckChannels(data[8], data[9], inputs, outputs) is { } channelError)
            return channelError;

        var offsetB = IccReader.ReadUInt32(data[12..]);
        var offsetMatrix = IccReader.ReadUInt32(data[16..]);
        var offsetM = IccReader.ReadUInt32(data[20..]);
        var offsetClut = IccReader.ReadUInt32(data[24..]);
        var offsetA = IccReader.ReadUInt32(data[28..]);
        var connectionChannels = deviceToConnection ? outputs : inputs;
        var deviceChannels = deviceToConnection ? inputs : outputs;

        IccStage? curvesA = null;
        IccStage? curvesM = null;
        IccStage? curvesB = null;
        IccStage? matrix = null;
        IccStage? clut = null;
        string? error = null;
        if (offsetA != 0)
        {
            error = ReadCurves(data, offsetA, deviceChannels, "A", out curvesA);
        }

        if (error is null && offsetM != 0)
        {
            error = ReadCurves(data, offsetM, connectionChannels, "M", out curvesM);
        }

        if (error is null && offsetB != 0)
        {
            error = ReadCurves(data, offsetB, connectionChannels, "B", out curvesB);
        }

        if (error is null && offsetMatrix != 0)
        {
            error = ReadMatrix(data, offsetMatrix, connectionChannels, out matrix);
        }

        if (error is null && offsetClut != 0)
        {
            error = ReadClut(tag, offsetClut, inputs, outputs, IsMultilinear(inputSpace, inputs), out clut);
        }
        else if (error is null && inputs != outputs)
        {
            error = "the lookup table has no color lookup table although its input and output channel counts differ.";
        }

        if (error is not null)
            return error;

        // The result of the matrix is clipped to [0, 1] before the next element (ICC.1:2022 section 10.12.5)
        ReadOnlySpan<IccStage?> elements = deviceToConnection
            ? [curvesA, clut, curvesM, matrix, matrix is null ? null : new IccClipStage(3), curvesB]
            : [curvesB, matrix, matrix is null ? null : new IccClipStage(3), curvesM, clut, curvesA];
        foreach (var element in elements)
        {
            if (element is not null)
            {
                stages.Add(element);
            }
        }

        return null;
    }

    /// <summary>Reads consecutive curve elements, each padded to a multiple of four bytes.</summary>
    private static string? ReadCurves(ReadOnlySpan<byte> data, uint offset, int count, string name, out IccStage? stage)
    {
        stage = null;
        var curves = new IccCurve[count];
        long position = offset;
        for (var i = 0; i < count; i++)
        {
            if (position > data.Length || !IccCurve.TryParse(data[(int)position..], out var curve, out var length))
                return string.Create(CultureInfo.InvariantCulture, $"curve {i} of the {name} curves is not a valid curveType or parametricCurveType inside the tag.");

            curves[i] = curve;
            position += (length + 3) & ~3;
        }

        stage = new IccCurvesStage(curves);
        return null;
    }

    /// <summary>Reads the matrix element: a 3x3 matrix in row-major order followed by three offsets, as s15Fixed16 numbers.</summary>
    private static string? ReadMatrix(ReadOnlySpan<byte> data, uint offset, int channels, out IccStage? stage)
    {
        stage = null;
        if (channels != 3)
            return "the lookup table has a matrix although the profile connection space side does not have three channels.";

        if (offset > data.Length || data.Length - (int)offset < 48)
            return "the matrix of the lookup table exceeds the tag.";

        Span<double> values = stackalloc double[12];
        for (var i = 0; i < 12; i++)
        {
            values[i] = IccReader.ReadS15Fixed16(data[((int)offset + (4 * i))..]);
        }

        stage = new IccMatrixStage(values[..9], values[9..]);
        return null;
    }

    /// <summary>Reads the color lookup table element: 16 grid point counts, the entry size (1 or 2 bytes), then the entries.</summary>
    private static string? ReadClut(ReadOnlyMemory<byte> tag, uint offset, int inputs, int outputs, bool multilinear, out IccStage? stage)
    {
        stage = null;
        var data = tag.Span;
        if (offset > data.Length || data.Length - (int)offset < ClutHeaderSize)
            return "the color lookup table exceeds the tag.";

        var header = data.Slice((int)offset, ClutHeaderSize);
        int bytesPerEntry = header[16];
        if (bytesPerEntry is not (1 or 2))
            return string.Create(CultureInfo.InvariantCulture, $"the color lookup table has entries of {bytesPerEntry} bytes; 1 or 2 are allowed.");

        Span<int> grid = stackalloc int[inputs];
        long length = outputs * bytesPerEntry;
        for (var i = 0; i < inputs; i++)
        {
            grid[i] = header[i];
            if (grid[i] < 2)
                return string.Create(CultureInfo.InvariantCulture, $"the color lookup table has {grid[i]} grid points for input {i}; at least 2 are required.");

            length *= grid[i];
        }

        if (length > data.Length - (int)offset - ClutHeaderSize)
            return "the color lookup table exceeds the tag.";

        stage = new IccClutStage(tag.Slice((int)offset + ClutHeaderSize, (int)length), grid, outputs, bytesPerEntry, multilinear);
        return null;
    }

    private static string? CheckChannels(int actualInputs, int actualOutputs, int inputs, int outputs)
    {
        if (actualInputs != inputs || actualOutputs != outputs)
            return string.Create(CultureInfo.InvariantCulture, $"the lookup table has {actualInputs} input and {actualOutputs} output channels; {inputs} and {outputs} are expected for this profile.");

        return null;
    }

    private static IccCurve[] ReadTables(ReadOnlySpan<byte> data, int count, int entries, int bytesPerEntry)
    {
        var curves = new IccCurve[count];
        for (var i = 0; i < count; i++)
        {
            var table = new double[entries];
            var source = data.Slice(i * entries * bytesPerEntry, entries * bytesPerEntry);
            for (var entry = 0; entry < entries; entry++)
            {
                table[entry] = bytesPerEntry == 1 ? source[entry] / 255.0 : IccReader.ReadUInt16(source[(2 * entry)..]) / 65535.0;
            }

            curves[i] = IccCurve.FromTable(table);
        }

        return curves;
    }
}
