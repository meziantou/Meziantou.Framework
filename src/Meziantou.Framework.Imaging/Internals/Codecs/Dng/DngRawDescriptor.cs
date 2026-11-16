namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The raw image of a DNG file, resolved and validated without reading a sample: the TIFF layout of its samples
/// (<see cref="TiffPage.ResolveDngRaw"/>), its color filter array, and the tags of chapter 5 of the DNG specification 1.7.1
/// that map its stored values to linear reference values (linearization, black and white levels), its active area and
/// its default crop.
/// </summary>
internal sealed class DngRawDescriptor : IDisposable
{
    private readonly List<AllocationCharge> _opcodeListCharges = [];

    private DngRawDescriptor(TiffPage page)
    {
        Page = page;
    }

    /// <summary>Gets the layout of the stored samples.</summary>
    public TiffPage Page { get; }

    /// <summary>Gets the size of the stored image, in pixels.</summary>
    public Size Size => Page.Size;

    /// <summary>Gets the number of samples of a stored pixel: 1 for a color filter array.</summary>
    public int SamplesPerPixel => Page.SamplesPerPixel;

    /// <summary>Gets a value indicating whether the samples are those of a color filter array.</summary>
    public bool IsCfa => Page.Photometric == TiffFormat.PhotometricCfa;

    /// <summary>Gets a value indicating whether the samples are floating-point numbers.</summary>
    public bool IsFloatingPoint => Page.IsFloatingPoint;

    /// <summary>Gets the number of color planes of the camera color space: the colors of the pattern, or the samples of linear raw data.</summary>
    public int Planes { get; private set; }

    /// <summary>Gets the number of rows of the repeating pattern of the color filter array (1 without one).</summary>
    public int PatternRows { get; private set; } = 1;

    /// <summary>Gets the number of columns of the repeating pattern of the color filter array (1 without one).</summary>
    public int PatternColumns { get; private set; } = 1;

    /// <summary>Gets the plane of each position of the pattern, row by row (the indexes of <see cref="PlaneColors"/>).</summary>
    public byte[] Pattern { get; private set; } = [];

    /// <summary>Gets the CFA color of each plane (0 red, 1 green, 2 blue, 3 cyan, 4 magenta, 5 yellow, 6 white), from <c>CFAPlaneColor</c>.</summary>
    public byte[] PlaneColors { get; private set; } = [];

    /// <summary>Gets the linearization table, or <see langword="null"/>.</summary>
    public ushort[]? LinearizationTable { get; private set; }

    /// <summary>Gets the number of rows of the repeating pattern of the black levels.</summary>
    public int BlackLevelRows { get; private set; } = 1;

    /// <summary>Gets the number of columns of the repeating pattern of the black levels.</summary>
    public int BlackLevelColumns { get; private set; } = 1;

    /// <summary>Gets the black levels of the repeating pattern, in row, column, sample order.</summary>
    public double[] BlackLevels { get; private set; } = [];

    /// <summary>Gets the black level added for each column of the active area, or <see langword="null"/>.</summary>
    public double[]? BlackLevelDeltaH { get; private set; }

    /// <summary>Gets the black level added for each row of the active area, or <see langword="null"/>.</summary>
    public double[]? BlackLevelDeltaV { get; private set; }

    /// <summary>Gets the white level of each sample.</summary>
    public double[] WhiteLevels { get; private set; } = [];

    /// <summary>Gets the active area: the rectangle of the stored image that holds the image (the masked pixels are outside).</summary>
    public Rectangle ActiveArea { get; private set; }

    /// <summary>Gets the default crop, relative to the top-left corner of the active area.</summary>
    public Rectangle DefaultCrop { get; private set; }

    /// <summary>Gets the number of fields the rows are stored in (<c>RowInterleaveFactor</c>, 1 for none).</summary>
    public int RowInterleaveFactor { get; private set; } = 1;

    /// <summary>Gets the number of fields the columns are stored in (<c>ColumnInterleaveFactor</c>, 1 for none).</summary>
    public int ColumnInterleaveFactor { get; private set; } = 1;

    /// <summary>Gets the opcode lists 1, 2 and 3, each <see langword="null"/> when absent.</summary>
    public DngOpcodeList?[] OpcodeLists { get; } = new DngOpcodeList?[3];

    /// <summary>Gets the plane of a pixel of the color filter array, from its coordinates relative to the active area.</summary>
    public int GetPlane(int x, int y) => Pattern[((y % PatternRows) * PatternColumns) + (x % PatternColumns)];

    /// <summary>Resolves the raw image of a DNG file.</summary>
    /// <param name="directory">The raw IFD.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidImageContentException">A tag is missing, malformed, or inconsistent with the rest of the raw image.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The raw image uses a recognized but unsupported variant.</exception>
    /// <exception cref="ImageResourceLimitException">The raw image exceeds a configured limit.</exception>
    public static DngRawDescriptor Resolve(DngDirectory directory, ImageCodecContext context)
    {
        var page = TiffPage.ResolveDngRaw(directory.Directory, directory.Index, context);
        var descriptor = new DngRawDescriptor(page);
        try
        {
            descriptor.ResolveCore(directory, context);
            return descriptor;
        }
        catch
        {
            descriptor.Dispose();
            throw;
        }
    }

    /// <summary>Releases the charge of the opcode lists.</summary>
    public void Dispose()
    {
        foreach (var charge in _opcodeListCharges)
        {
            charge.Dispose();
        }

        _opcodeListCharges.Clear();
    }

    private void ResolveCore(DngDirectory directory, ImageCodecContext context)
    {
        var page = Page;

        // Every strip or tile is inside the input: a file cut in its samples is rejected without reading them
        var length = (ulong)directory.Directory.Source.Length;
        for (var i = 0; i < page.BlockOffsets.Length; i++)
        {
            if (page.BlockOffsets[i] > length || length - page.BlockOffsets[i] < page.BlockByteCounts[i])
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The block {i} of the raw image of the DNG file ({page.BlockByteCounts[i]} bytes at offset {page.BlockOffsets[i]}) is outside the input of {length} bytes."));
        }

        ResolveColorFilterArray();
        ResolveInterleaving();
        ResolveAreas();
        ResolveLevels(context);
        ResolveOpcodeLists(context);
    }

    private TiffDirectory Directory => Page.Directory;

    private void ResolveColorFilterArray()
    {
        if (!IsCfa)
        {
            Planes = SamplesPerPixel;
            return;
        }

        var layout = DngTagReader.TryReadNumber(Directory, DngFormat.CfaLayoutTag, "CFALayout") ?? DngFormat.CfaLayoutRectangular;
        if (layout != DngFormat.CfaLayoutRectangular)
            throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The color filter array of the DNG file uses the CFALayout {layout} (a staggered layout); the rectangular layout (1) is supported."), string.Create(CultureInfo.InvariantCulture, $"CFALayout: {layout}"));

        var dimensions = DngTagReader.TryReadIntegers(Directory, DngFormat.CfaRepeatPatternDimTag, "CFARepeatPatternDim", 2)
            ?? throw DngFormat.Invalid("The color filter array of the DNG file has no CFARepeatPatternDim tag, which is required.");
        if (dimensions.Length != 2 || dimensions[0] is < 1 or > DngFormat.MaxPatternSize || dimensions[1] is < 1 or > DngFormat.MaxPatternSize)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The CFARepeatPatternDim of the DNG file is ({string.Join(", ", dimensions)}); two dimensions of 1 to {DngFormat.MaxPatternSize} are expected."));

        PatternRows = (int)dimensions[0];
        PatternColumns = (int)dimensions[1];
        var pattern = DngTagReader.TryReadBytes(Directory, DngFormat.CfaPatternTag, "CFAPattern", PatternRows * PatternColumns)
            ?? throw DngFormat.Invalid("The color filter array of the DNG file has no CFAPattern tag, which is required.");
        if (pattern.Length != PatternRows * PatternColumns)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The CFAPattern of the DNG file has {pattern.Length} values; its {PatternRows}x{PatternColumns} repeating pattern needs {PatternRows * PatternColumns}."));

        var planeColors = DngTagReader.TryReadBytes(Directory, DngFormat.CfaPlaneColorTag, "CFAPlaneColor", 4) ?? [0, 1, 2];
        if (planeColors.Length is < 1 or > 4)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The CFAPlaneColor of the DNG file has {planeColors.Length} colors; 1 to 4 are supported."));

        for (var i = 0; i < planeColors.Length; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (planeColors[i] == planeColors[j])
                    throw DngFormat.Invalid("The CFAPlaneColor of the DNG file names the same color twice.");
            }
        }

        // The pattern names CFA colors; each is a plane of the camera color space
        var planes = new byte[pattern.Length];
        var used = new bool[planeColors.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            var plane = Array.IndexOf(planeColors, pattern[i]);
            if (plane < 0)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The CFAPattern of the DNG file uses the color {pattern[i]}, which CFAPlaneColor does not list."));

            planes[i] = (byte)plane;
            used[plane] = true;
        }

        if (Array.IndexOf(used, false) >= 0)
            throw DngFormat.Invalid("A color of the CFAPlaneColor of the DNG file is not in its CFAPattern.");

        Pattern = planes;
        PlaneColors = planeColors;
        Planes = planeColors.Length;
    }

    private void ResolveInterleaving()
    {
        RowInterleaveFactor = ReadFactor(DngFormat.RowInterleaveFactorTag, "RowInterleaveFactor", Size.Height);
        ColumnInterleaveFactor = ReadFactor(DngFormat.ColumnInterleaveFactorTag, "ColumnInterleaveFactor", Size.Width);
        if (DngTagReader.TryReadIntegers(Directory, DngFormat.SubTileBlockSizeTag, "SubTileBlockSize", 2) is { } subTile && (subTile.Length != 2 || subTile[0] != 1 || subTile[1] != 1))
            throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image of the DNG file stores its tiles in sub-tile blocks of {string.Join("x", subTile)} pixels (SubTileBlockSize), which is not supported."), "SubTileBlockSize");

        int ReadFactor(ushort tag, string name, int length)
        {
            var value = DngTagReader.TryReadIntegers(Directory, tag, name, 1)?[0] ?? 1;
            if (value < 1 || value > length)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {name} of the DNG file is {value}; 1 to {length} fields are allowed."));

            return (int)value;
        }
    }

    private void ResolveAreas()
    {
        var area = DngTagReader.TryReadIntegers(Directory, DngFormat.ActiveAreaTag, "ActiveArea", 4);
        if (area is null)
        {
            ActiveArea = new Rectangle(0, 0, Size.Width, Size.Height);
        }
        else
        {
            // Top, left, bottom, right
            if (area.Length != 4 || area[0] >= area[2] || area[1] >= area[3] || area[2] > Size.Height || area[3] > Size.Width)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The ActiveArea of the DNG file ({string.Join(", ", area)}) is not a rectangle of its {Size.Width}x{Size.Height} raw image."));

            ActiveArea = Rectangle.FromLTRB((int)area[1], (int)area[0], (int)area[3], (int)area[2]);
        }

        var scale = DngTagReader.TryReadExactNumbers(Directory, DngFormat.DefaultScaleTag, "DefaultScale", 2);
        if (scale is not null && (scale[0] != 1 || scale[1] != 1))
            throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DNG file has non-square pixels (DefaultScale {scale[0]}, {scale[1]}), which are not resampled."), "DefaultScale");

        // The crop is in raw image coordinates relative to the active area; fractions are rounded to the nearest pixel
        var origin = DngTagReader.TryReadExactNumbers(Directory, DngFormat.DefaultCropOriginTag, "DefaultCropOrigin", 2) ?? [0, 0];
        var size = DngTagReader.TryReadExactNumbers(Directory, DngFormat.DefaultCropSizeTag, "DefaultCropSize", 2) ?? [ActiveArea.Width - Math.Round(origin[0], MidpointRounding.AwayFromZero), ActiveArea.Height - Math.Round(origin[1], MidpointRounding.AwayFromZero)];
        var left = Math.Round(origin[0], MidpointRounding.AwayFromZero);
        var top = Math.Round(origin[1], MidpointRounding.AwayFromZero);
        var width = Math.Round(size[0], MidpointRounding.AwayFromZero);
        var height = Math.Round(size[1], MidpointRounding.AwayFromZero);
        if (left < 0 || top < 0 || width < 1 || height < 1 || left + width > ActiveArea.Width || top + height > ActiveArea.Height)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The default crop of the DNG file (origin {origin[0]}, {origin[1]}, size {size[0]}x{size[1]}) is not inside its {ActiveArea.Width}x{ActiveArea.Height} active area."));

        DefaultCrop = new Rectangle((int)left, (int)top, (int)width, (int)height);
    }

    private void ResolveLevels(ImageCodecContext context)
    {
        var samples = SamplesPerPixel;
        var maxStored = IsFloatingPoint ? 1.0 : (1L << Page.BitsPerSample) - 1;
        if (Directory.Find(DngFormat.LinearizationTableTag) is { } field)
        {
            if (IsFloatingPoint)
                throw DngFormat.Invalid("The DNG file has a LinearizationTable for floating-point samples.");

            if (field.Type != TiffFieldType.Short || field.Count is 0 or > DngFormat.MaxLinearizationTableLength)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The LinearizationTable of the DNG file declares {field.Count} values of {TiffFieldTypes.GetName(field.Type)}; 1 to {DngFormat.MaxLinearizationTableLength} SHORT values are expected."));

            context.Tracker.ChargeMetadataBytes((long)field.Count * 2);
            var values = Directory.ReadUnsignedArray(field, (long)field.Count);
            LinearizationTable = [.. values.Select(value => (ushort)value)];
            maxStored = LinearizationTable.Max();
        }

        var repeat = DngTagReader.TryReadIntegers(Directory, DngFormat.BlackLevelRepeatDimTag, "BlackLevelRepeatDim", 2) ?? [1, 1];
        if (repeat.Length != 2 || repeat[0] is < 1 or > DngFormat.MaxPatternSize || repeat[1] is < 1 or > DngFormat.MaxPatternSize)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BlackLevelRepeatDim of the DNG file is ({string.Join(", ", repeat)}); two dimensions of 1 to {DngFormat.MaxPatternSize} are expected."));

        BlackLevelRows = (int)repeat[0];
        BlackLevelColumns = (int)repeat[1];
        var count = BlackLevelRows * BlackLevelColumns * samples;
        BlackLevels = DngTagReader.TryReadExactNumbers(Directory, DngFormat.BlackLevelTag, "BlackLevel", count) ?? new double[count];
        BlackLevelDeltaH = DngTagReader.TryReadExactNumbers(Directory, DngFormat.BlackLevelDeltaHTag, "BlackLevelDeltaH", ActiveArea.Width);
        BlackLevelDeltaV = DngTagReader.TryReadExactNumbers(Directory, DngFormat.BlackLevelDeltaVTag, "BlackLevelDeltaV", ActiveArea.Height);
        if (BlackLevelDeltaH is not null || BlackLevelDeltaV is not null)
        {
            context.Tracker.ChargeMetadataBytes(((BlackLevelDeltaH?.Length ?? 0) + (BlackLevelDeltaV?.Length ?? 0)) * 8L);
        }

        WhiteLevels = DngTagReader.TryReadExactNumbers(Directory, DngFormat.WhiteLevelTag, "WhiteLevel", samples) ?? [.. Enumerable.Repeat(maxStored, samples)];
        for (var sample = 0; sample < samples; sample++)
        {
            if (!(WhiteLevels[sample] > GetMaxBlackLevel(sample)))
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The white level of the sample {sample} of the DNG file ({WhiteLevels[sample]}) is not above its black level ({GetMaxBlackLevel(sample)})."));
        }
    }

    /// <summary>Gets the largest black level of a sample plane: the scale of chapter 5 is the inverse of the white level minus it.</summary>
    public double GetMaxBlackLevel(int sample)
    {
        var max = double.NegativeInfinity;
        for (var i = sample; i < BlackLevels.Length; i += SamplesPerPixel)
        {
            max = Math.Max(max, BlackLevels[i]);
        }

        return max + (BlackLevelDeltaH is { Length: > 0 } h ? h.Max() : 0) + (BlackLevelDeltaV is { Length: > 0 } v ? v.Max() : 0);
    }

    private void ResolveOpcodeLists(ImageCodecContext context)
    {
        ReadOnlySpan<ushort> tags = [DngFormat.OpcodeList1Tag, DngFormat.OpcodeList2Tag, DngFormat.OpcodeList3Tag];
        for (var i = 0; i < tags.Length; i++)
        {
            if (Directory.Find(tags[i]) is not { } field)
                continue;

            context.Tracker.ChargeMetadataBytes((long)field.Count);

            // The arrays of the parsed opcodes take at most as many bytes as their parameters (a table entry, a gain or
            // a coordinate is stored in as many bytes as it is kept in): the list is charged twice its size, for the copy
            // of its bytes while it is parsed and for its opcodes, which stay charged until the descriptor is disposed
            var name = "OpcodeList" + (i + 1).ToString(CultureInfo.InvariantCulture);
            var retained = context.Scope.Charge((long)field.Count, AllocationKind.DecoderState);
            try
            {
                using (context.Scope.Charge((long)field.Count, AllocationKind.Temporary))
                {
                    var data = DngTagReader.TryReadBytes(Directory, tags[i], name, context.Limits.MaxMetadataBytes);
                    OpcodeLists[i] = DngOpcodeList.Parse(data, i + 1, context.IsLenient);
                }
            }
            catch
            {
                retained.Dispose();
                throw;
            }

            _opcodeListCharges.Add(retained);
        }
    }
}
