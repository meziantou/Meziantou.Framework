namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The stages of the DNG processing model, as the DNG specification 1.7.1 orders them: the stored samples (stage 1) and
/// opcode list 1; the mapping to linear reference values of chapter 5 over the active area (stage 2) and opcode list 2;
/// the demosaicing (stage 3) and opcode list 3; the default crop and the color transform of chapter 6.
/// </summary>
/// <remarks>
/// <para>
/// Stage 2: a stored value goes through the linearization table (a value past its end takes its last entry), then
/// <c>(value - black) / (white - maxBlack)</c>, where the black level of a pixel is the one of its position in the
/// repeating pattern of <c>BlackLevel</c> (whose origin is the top-left corner of the active area) plus
/// <c>BlackLevelDeltaH</c> of its column and <c>BlackLevelDeltaV</c> of its row, and <c>maxBlack</c> the largest black
/// level of its sample plane. The result is clipped to [0, 1], and each stage holds single-precision values computed in
/// double precision.
/// </para>
/// <para>
/// Stage 3, the demosaicing of a color filter array (<see cref="Demosaic"/>): the color of a pixel is its sample; another
/// color is the mean of the samples of that color in the smallest square window centered on the pixel (3x3, 5x5, ...)
/// that holds at least one of them inside the active area. For a Bayer pattern, this is the bilinear interpolation: the
/// mean of the two or four nearest samples of the color. Linear raw data is its own stage 3.
/// </para>
/// </remarks>
internal static class DngPipeline
{
    /// <summary>Reads the stored samples and applies opcode list 1: stage 1.</summary>
    public static DngPlanes ReadStage1(RandomAccessSource source, DngRawDescriptor raw, ImageCodecContext context)
    {
        var stage1 = DngSampleReader.Read(source, raw, context);
        try
        {
            raw.OpcodeLists[0]?.Apply(stage1, raw.IsFloatingPoint ? DngSampleValues.StoredFloats : DngSampleValues.StoredIntegers, context);
            return stage1;
        }
        catch
        {
            stage1.Dispose();
            throw;
        }
    }

    /// <summary>Maps the stored values of the active area to linear reference values and applies opcode list 2: stage 2.</summary>
    public static DngPlanes Linearize(DngPlanes stage1, DngRawDescriptor raw, ImageCodecContext context)
    {
        var area = raw.ActiveArea;
        var samples = raw.SamplesPerPixel;
        var stage2 = DngPlanes.Create(context, area.Width, area.Height, samples);
        try
        {
            Span<double> scale = stackalloc double[samples];
            for (var sample = 0; sample < samples; sample++)
            {
                scale[sample] = 1 / (raw.WhiteLevels[sample] - raw.GetMaxBlackLevel(sample));
            }

            var table = raw.LinearizationTable;
            var levels = raw.BlackLevels;
            var deltaH = raw.BlackLevelDeltaH;
            var deltaV = raw.BlackLevelDeltaV;
            for (var y = 0; y < area.Height; y++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var source = stage1.GetRow(area.Y + y).Slice(area.X * samples, area.Width * samples);
                var destination = stage2.GetRow(y);
                var levelRow = (y % raw.BlackLevelRows) * raw.BlackLevelColumns;
                var rowDelta = deltaV?[y] ?? 0;
                for (var x = 0; x < area.Width; x++)
                {
                    var levelIndex = (levelRow + (x % raw.BlackLevelColumns)) * samples;
                    var columnDelta = deltaH?[x] ?? 0;
                    for (var sample = 0; sample < samples; sample++)
                    {
                        double value = source[(x * samples) + sample];
                        if (raw.IsFloatingPoint && !double.IsFinite(value))
                            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The raw image of the DNG file stores the floating-point sample {value} at ({area.X + x}, {area.Y + y}), which is not a light level."));

                        if (!raw.IsFloatingPoint)
                        {
                            var stored = DngSampleReader.Denormalize((float)value);
                            value = table is null ? stored : table[Math.Min(stored, table.Length - 1)];
                        }

                        var black = levels[levelIndex + sample] + columnDelta + rowDelta;
                        destination[(x * samples) + sample] = (float)Math.Clamp((value - black) * scale[sample], 0, 1);
                    }
                }
            }

            raw.OpcodeLists[1]?.Apply(stage2, DngSampleValues.Linear, context);
            return stage2;
        }
        catch
        {
            stage2.Dispose();
            throw;
        }
    }

    /// <summary>Demosaics a color filter array (or takes linear raw data as it is) and applies opcode list 3: stage 3.</summary>
    /// <returns>Stage 3, or <paramref name="stage2"/> itself (linear raw data without opcode list 3).</returns>
    public static DngPlanes Demosaic(DngPlanes stage2, DngRawDescriptor raw, ImageCodecContext context)
    {
        if (!raw.IsCfa)
        {
            raw.OpcodeLists[2]?.Apply(stage2, DngSampleValues.Linear, context);
            return stage2;
        }

        var width = stage2.Width;
        var height = stage2.Height;
        var planes = raw.Planes;
        // Every color must have a sample in the image, or no window would ever hold one: the pattern repeats, so its first
        // period (clipped to the image) holds every color the image has
        var present = 0;
        for (var y = 0; y < Math.Min(height, raw.PatternRows); y++)
        {
            for (var x = 0; x < Math.Min(width, raw.PatternColumns); x++)
            {
                present |= 1 << raw.GetPlane(x, y);
            }
        }

        for (var plane = 0; plane < planes; plane++)
        {
            if ((present & (1 << plane)) == 0)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {width}x{height} active area of the DNG file holds no sample of the color {plane} of its color filter array: it cannot be demosaiced."));
        }

        var stage3 = DngPlanes.Create(context, width, height, planes);
        try
        {
            // The window radius each color of each position of the pattern needs inside the image, from the pattern alone
            var radii = new int[raw.PatternRows * raw.PatternColumns * planes];
            for (var py = 0; py < raw.PatternRows; py++)
            {
                for (var px = 0; px < raw.PatternColumns; px++)
                {
                    for (var plane = 0; plane < planes; plane++)
                    {
                        var radius = 0;
                        while (!HasColor(raw, px + (raw.PatternColumns * DngFormat.MaxPatternSize), py + (raw.PatternRows * DngFormat.MaxPatternSize), radius, plane))
                        {
                            radius++;
                        }

                        radii[(((py * raw.PatternColumns) + px) * planes) + plane] = radius;
                    }
                }
            }

            var cfa = stage2.Samples;
            var output = stage3.Samples;
            for (var y = 0; y < height; y++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var own = raw.GetPlane(x, y);
                    var patternIndex = ((y % raw.PatternRows) * raw.PatternColumns) + (x % raw.PatternColumns);
                    for (var plane = 0; plane < planes; plane++)
                    {
                        var target = (((y * width) + x) * planes) + plane;
                        if (plane == own)
                        {
                            output[target] = cfa[(y * width) + x];
                            continue;
                        }

                        // The smallest window that holds a sample of the color inside the image
                        for (var radius = radii[(patternIndex * planes) + plane]; ; radius++)
                        {
                            var sum = 0.0;
                            var count = 0;
                            var top = Math.Max(0, y - radius);
                            var bottom = Math.Min(height - 1, y + radius);
                            var left = Math.Max(0, x - radius);
                            var right = Math.Min(width - 1, x + radius);
                            for (var wy = top; wy <= bottom; wy++)
                            {
                                for (var wx = left; wx <= right; wx++)
                                {
                                    if (raw.GetPlane(wx, wy) == plane)
                                    {
                                        sum += cfa[(wy * width) + wx];
                                        count++;
                                    }
                                }
                            }

                            if (count > 0)
                            {
                                output[target] = (float)(sum / count);
                                break;
                            }
                        }
                    }
                }
            }

            raw.OpcodeLists[2]?.Apply(stage3, DngSampleValues.Linear, context);
            return stage3;
        }
        catch
        {
            stage3.Dispose();
            throw;
        }
    }

    /// <summary>Determines whether the square window of a radius around a position of the (unbounded) pattern holds a color.</summary>
    private static bool HasColor(DngRawDescriptor raw, int x, int y, int radius, int plane)
    {
        for (var wy = y - radius; wy <= y + radius; wy++)
        {
            for (var wx = x - radius; wx <= x + radius; wx++)
            {
                if ((wx != x || wy != y) && raw.GetPlane(wx, wy) == plane)
                    return true;
            }
        }

        return false;
    }
}
