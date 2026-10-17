namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding parameters of one tile, resolved from the main header and the headers of its tile-parts (T.800, A.3): for a
/// component, a COC marker segment of the tile overrides the COD marker segment of the tile, which overrides the COC marker
/// segment of the main header, which overrides its COD marker segment; the quantization and the regions of interest follow
/// the same order.
/// </summary>
internal sealed class Jpeg2000TileCoding
{
    private Jpeg2000TileCoding(Jpeg2000CodingDefaults defaults, Jpeg2000CodingStyle[] styles, Jpeg2000Quantization[] quantizations, int[] regionShifts, IReadOnlyList<Jpeg2000ProgressionChange>? progressions, Jpeg2000Tools tools)
    {
        Defaults = defaults;
        Styles = styles;
        Quantizations = quantizations;
        RegionShifts = regionShifts;
        Progressions = progressions;
        Tools = tools;
    }

    /// <summary>Gets the COD marker segment that applies to the tile: the progression, the layers, the component transformation and the packet markers.</summary>
    public Jpeg2000CodingDefaults Defaults { get; }

    /// <summary>Gets the coding style of each component.</summary>
    public Jpeg2000CodingStyle[] Styles { get; }

    /// <summary>Gets the quantization of each component.</summary>
    public Jpeg2000Quantization[] Quantizations { get; }

    /// <summary>Gets the shift of the region of interest of each component; 0 when there is none.</summary>
    public int[] RegionShifts { get; }

    /// <summary>Gets the progressions of the POC marker segments, or <see langword="null"/> when the progression is the one of <see cref="Defaults"/>.</summary>
    public IReadOnlyList<Jpeg2000ProgressionChange>? Progressions { get; }

    /// <summary>Gets the features the parameters use.</summary>
    public Jpeg2000Tools Tools { get; }

    /// <summary>Gets the number of magnitude bit-planes of a sub-band, without the shift of the region of interest (equation E-2).</summary>
    /// <param name="component">The component.</param>
    /// <param name="bandIndex">The index of the sub-band: 0 for the LL sub-band, then the HL, LH and HH sub-bands of each resolution level from the lowest.</param>
    public int GetBitPlanes(int component, int bandIndex)
    {
        var quantization = Quantizations[component];
        quantization.GetStep(bandIndex, out var exponent, out _);
        return Math.Max(0, quantization.GuardBits + exponent - 1);
    }

    /// <summary>Resolves and validates the parameters of a tile.</summary>
    /// <param name="siz">The SIZ marker segment.</param>
    /// <param name="main">The parameters of the main header.</param>
    /// <param name="tile">The parameters of the tile-part headers of the tile, or <see langword="null"/>.</param>
    /// <param name="capabilities">The CAP marker segment, or <see langword="null"/>.</param>
    /// <exception cref="InvalidImageContentException">The parameters are inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The parameters use a feature that is not supported.</exception>
    public static Jpeg2000TileCoding Resolve(Jpeg2000Siz siz, Jpeg2000HeaderSet main, Jpeg2000HeaderSet? tile, Jpeg2000Capabilities? capabilities)
    {
        var defaults = tile?.CodingDefaults ?? main.CodingDefaults ?? throw Jpeg2000Format.Invalid("the main header has no COD marker segment.");
        var mainQuantization = main.Quantization ?? throw Jpeg2000Format.Invalid("the main header has no QCD marker segment.");
        var tools = Jpeg2000Tools.None;
        if (tile is not null)
        {
            if (tile.CodingDefaults is not null)
            {
                tools |= Jpeg2000Tools.TileCodingStyle;
            }

            if (tile.Quantization is not null)
            {
                tools |= Jpeg2000Tools.TileQuantization;
            }
        }

        tools |= ProgressionTool(defaults.Progression);
        if (defaults.Layers > 1)
        {
            tools |= Jpeg2000Tools.Layers;
        }

        if (defaults.UsesEph)
        {
            tools |= Jpeg2000Tools.EndOfPacketHeader;
        }

        var componentCount = siz.Components.Length;
        var styles = new Jpeg2000CodingStyle[componentCount];
        var quantizations = new Jpeg2000Quantization[componentCount];
        var regionShifts = new int[componentCount];
        for (var c = 0; c < componentCount; c++)
        {
            Jpeg2000CodingStyle style;
            if (tile?.GetComponentStyle(c) is { } tileComponentStyle)
            {
                style = tileComponentStyle;
                tools |= Jpeg2000Tools.ComponentCodingStyle | Jpeg2000Tools.TileCodingStyle;
            }
            else if (tile?.CodingDefaults is { } tileDefaults)
            {
                style = tileDefaults.Style;
            }
            else if (main.GetComponentStyle(c) is { } mainComponentStyle)
            {
                style = mainComponentStyle;
                tools |= Jpeg2000Tools.ComponentCodingStyle;
            }
            else
            {
                style = main.CodingDefaults!.Style;
            }

            Jpeg2000Quantization quantization;
            if (tile?.GetComponentQuantization(c) is { } tileComponentQuantization)
            {
                quantization = tileComponentQuantization;
                tools |= Jpeg2000Tools.ComponentQuantization | Jpeg2000Tools.TileQuantization;
            }
            else if (tile?.Quantization is { } tileQuantization)
            {
                quantization = tileQuantization;
            }
            else if (main.GetComponentQuantization(c) is { } mainComponentQuantization)
            {
                quantization = mainComponentQuantization;
                tools |= Jpeg2000Tools.ComponentQuantization;
            }
            else
            {
                quantization = mainQuantization;
            }

            var shift = tile?.GetRegionShift(c) ?? -1;
            if (shift < 0)
            {
                shift = Math.Max(0, main.GetRegionShift(c));
            }

            styles[c] = style;
            quantizations[c] = quantization;
            regionShifts[c] = shift;
            tools |= Validate(c, style, quantization, shift, capabilities);
        }

        if (defaults.UsesComponentTransform)
        {
            // G.2 and G.3: the transformation applies to the first three components, which have the same size and depth
            if (componentCount < 3)
                throw Jpeg2000Format.Invalid("the codestream uses the multiple component transformation with fewer than three components.");

            var first = siz.Components[0];
            for (var c = 1; c < 3; c++)
            {
                if (siz.Components[c] != first)
                    throw Jpeg2000Format.Invalid("the codestream uses the multiple component transformation on components that differ in size or in depth.");

                if (styles[c].IsReversible != styles[0].IsReversible)
                    throw Jpeg2000Format.Invalid("the codestream uses the multiple component transformation on components that differ in wavelet transformation.");
            }

            tools |= styles[0].IsReversible ? Jpeg2000Tools.ReversibleComponentTransform : Jpeg2000Tools.IrreversibleComponentTransform;
        }

        var progressions = tile?.Progressions ?? main.Progressions;
        if (progressions is not null)
        {
            tools |= Jpeg2000Tools.ProgressionChange;
            foreach (var progression in progressions)
            {
                tools |= ProgressionTool(progression.Progression);
            }
        }

        return new Jpeg2000TileCoding(defaults, styles, quantizations, regionShifts, progressions, tools);
    }

    private static Jpeg2000Tools Validate(int component, Jpeg2000CodingStyle style, Jpeg2000Quantization quantization, int regionShift, Jpeg2000Capabilities? capabilities)
    {
        // Table A.28: the reversible transformation has no quantization, the irreversible one has a scalar quantization
        if (style.IsReversible != (quantization.Style == Jpeg2000Quantization.StyleNone))
            throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the quantization style of the component {component} does not match its wavelet transformation."));

        if (!quantization.Covers(style.DecompositionLevels))
            throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the quantization of the component {component} describes fewer sub-bands than its {style.DecompositionLevels} decomposition levels have."));

        var bandCount = (3 * style.DecompositionLevels) + 1;
        for (var band = 0; band < bandCount; band++)
        {
            quantization.GetStep(band, out var exponent, out _);
            if (exponent < 0)
                throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the derived quantization of the component {component} gives a negative exponent to a sub-band."));

            var bitPlanes = quantization.GuardBits + exponent - 1 + regionShift;
            if (bitPlanes > Jpeg2000Format.MaxBitPlanes)
                throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream codes {bitPlanes} magnitude bit-planes for a sub-band; at most {Jpeg2000Format.MaxBitPlanes} are supported."), "bit-planes");
        }

        var tools = style.IsReversible ? Jpeg2000Tools.Reversible : Jpeg2000Tools.Irreversible;
        tools |= quantization.Style switch
        {
            Jpeg2000Quantization.StyleNone => Jpeg2000Tools.QuantizationNone,
            Jpeg2000Quantization.StyleDerived => Jpeg2000Tools.QuantizationDerived,
            _ => Jpeg2000Tools.QuantizationExpounded,
        };

        if (regionShift > 0)
        {
            tools |= Jpeg2000Tools.RegionOfInterest;
        }

        var blockStyle = style.CodeBlockStyle;
        if ((blockStyle & (Jpeg2000CodingStyle.StyleHighThroughput | Jpeg2000CodingStyle.StyleMixed)) != 0)
        {
            tools |= Jpeg2000FeaturePolicy.EnsureSupportedHighThroughputStyle(blockStyle, capabilities);
        }
        else
        {
            if ((blockStyle & Jpeg2000CodingStyle.StyleBypass) != 0)
            {
                tools |= Jpeg2000Tools.Bypass;
            }

            if ((blockStyle & Jpeg2000CodingStyle.StyleReset) != 0)
            {
                tools |= Jpeg2000Tools.ResetContexts;
            }

            if ((blockStyle & Jpeg2000CodingStyle.StyleTerminateAll) != 0)
            {
                tools |= Jpeg2000Tools.TerminateAll;
            }

            if ((blockStyle & Jpeg2000CodingStyle.StylePredictableTermination) != 0)
            {
                tools |= Jpeg2000Tools.PredictableTermination;
            }

            if ((blockStyle & Jpeg2000CodingStyle.StyleSegmentationSymbols) != 0)
            {
                tools |= Jpeg2000Tools.SegmentationSymbols;
            }
        }

        if ((blockStyle & Jpeg2000CodingStyle.StyleVerticallyCausal) != 0)
        {
            tools |= Jpeg2000Tools.VerticallyCausal;
        }

        return tools;
    }

    private static Jpeg2000Tools ProgressionTool(int progression) => progression switch
    {
        Jpeg2000CodingDefaults.ProgressionLrcp => Jpeg2000Tools.ProgressionLrcp,
        Jpeg2000CodingDefaults.ProgressionRlcp => Jpeg2000Tools.ProgressionRlcp,
        Jpeg2000CodingDefaults.ProgressionRpcl => Jpeg2000Tools.ProgressionRpcl,
        Jpeg2000CodingDefaults.ProgressionPcrl => Jpeg2000Tools.ProgressionPcrl,
        _ => Jpeg2000Tools.ProgressionCprl,
    };
}
