using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An ICC color conversion between two profiles: the ordered stages converting the normalized device values of the
/// source profile to those of the destination profile, through the profile connection space. The source may also be a
/// CICP color space, whose stages give the same connection space. Thread-safe: the conversion is immutable, and the
/// only state is the 8-bit output tables, built once (<see cref="GetByteOutputTables"/>).
/// </summary>
internal sealed class IccPipeline
{
    /// <summary>The largest number of components a stage reads or writes.</summary>
    public const int MaxChannels = 4;

    /// <summary>The number of colors that the kernels convert together, stored by component (<see cref="IccStage.ApplyBatch"/>).</summary>
    public const int BatchSize = 64;

    /// <summary>
    /// The colors whose output curves are evaluated before <see cref="GetByteOutputTables"/> builds the tables: building
    /// the table of a curve costs about 40,000 evaluations of the curve (0.3 to 1 ms), which saves 4 to 45 ns per sample
    /// depending on the curve, so the tables pay from 17,000 to 165,000 colors. Small images never pay for them, and
    /// large ones pay at most about twice the cheaper of both.
    /// </summary>
    internal const int ByteOutputTablesThreshold = 65_536;

    // The value of _byteOutputTables when a curve does not match its thresholds: the last stage is evaluated
    private static readonly QuantizationThresholds[] NoByteOutputTables = [];

    private readonly IccStage[] _stages;

    // The curves of the first stage when it is one curve per source channel (ByteInputTables): the tone curves, or the
    // CICP decoding of every channel
    private readonly IccCurve[]? _inputCurves;
    private readonly CicpDecodeStage? _inputDecode;

    // The inverse tone curves of the last stage, one per destination channel, when it is not the first stage and not
    // only identities (cheaper to evaluate than to look up)
    private readonly IccInverseCurve[]? _outputCurves;

    // The colors counted by GetByteOutputTables before the tables were built, then the tables (or NoByteOutputTables)
    private long _outputCurveColors;
    private QuantizationThresholds[]? _byteOutputTables;

    private IccPipeline(IccStage[] stages, int sourceChannelCount, int destinationChannelCount, bool isIdentity)
    {
        _stages = stages;
        SourceChannelCount = sourceChannelCount;
        DestinationChannelCount = destinationChannelCount;
        IsIdentity = isIdentity;
        HasLookupTable = Array.Exists(stages, stage => stage is IccClutStage);

        // The first stage of most conversions is one curve per source channel (tone curves, or the input tables of a
        // lookup table): its values for the 256 possible 8-bit samples are computed once, by the same evaluation as
        // IccCurvesStage on the same normalized sample, so that using the table gives bit-identical results
        if (stages.Length > 0 && stages[0] is IccCurvesStage { Curves: var curves } && curves.Length == sourceChannelCount)
        {
            _inputCurves = curves;
            var tables = new double[sourceChannelCount][];
            for (var channel = 0; channel < tables.Length; channel++)
            {
                var table = new double[256];
                for (var sample = 0; sample < table.Length; sample++)
                {
                    table[sample] = curves[channel].Evaluate(IccByteSample.Load((byte)sample));
                }

                tables[channel] = table;
            }

            ByteInputTables = tables;
        }
        else if (stages.Length > 0 && stages[0] is CicpDecodeStage decode && decode.ChannelCount == sourceChannelCount)
        {
            _inputDecode = decode;
            // The same function for every channel of a CICP source, evaluated as the stage does
            var table = new double[256];
            for (var sample = 0; sample < table.Length; sample++)
            {
                table[sample] = decode.Evaluate(IccByteSample.Load((byte)sample));
            }

            var tables = new double[sourceChannelCount][];
            tables.AsSpan().Fill(table);
            ByteInputTables = tables;
        }

        // The last stage of a matrix-based destination is one inverse tone curve per destination channel
        if (stages.Length > 1 && stages[^1] is IccInverseCurvesStage { Curves: var outputCurves } && outputCurves.Length == destinationChannelCount && !Array.TrueForAll(outputCurves, curve => curve.IsIdentity))
        {
            _outputCurves = outputCurves;
        }
    }

    /// <summary>
    /// Gets, when the first stage is one curve per source channel, the value of that stage for each 8-bit sample of each
    /// channel; <see langword="null"/> otherwise. See <see cref="ApplyAfterInputCurves"/>.
    /// </summary>
    public double[][]? ByteInputTables { get; }

    /// <summary>
    /// Gets a value indicating whether a stage interpolates a color lookup table: such a conversion costs far more per color
    /// than a matrix-based one, so remembering the colors already converted pays (<see cref="ColorConversionKernels"/>).
    /// </summary>
    public bool HasLookupTable { get; }

    /// <summary>Gets the inverse tone curves of the last stage that <see cref="GetByteOutputTables"/> replaces, one per destination channel; <see langword="null"/> when it does not.</summary>
    internal IccInverseCurve[]? OutputCurves => _outputCurves;

    /// <summary>Gets a value indicating whether the first stage is one curve per source channel (<see cref="ByteInputTables"/> is not <see langword="null"/>).</summary>
    public bool HasInputCurves => ByteInputTables is not null;

    /// <summary>Gets the number of stages.</summary>
    internal int StageCount => _stages.Length;

    /// <summary>Gets the number of device channels of the source profile.</summary>
    public int SourceChannelCount { get; }

    /// <summary>Gets the number of device channels of the destination profile.</summary>
    public int DestinationChannelCount { get; }

    /// <summary>Gets a value indicating whether the two profiles are identical, in which case the values are not changed.</summary>
    public bool IsIdentity { get; }

    /// <summary>Creates the conversion between two profiles.</summary>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A profile is valid but not supported in its role.</exception>
    public static IccPipeline Create(IccProfile source, IccProfile destination, IccRenderingIntent intent, bool blackPointCompensation)
    {
        var sourceModel = IccProfileModel.Create(source, "source");
        var destinationModel = IccProfileModel.Create(destination, "destination");
        if (source.Data.Equals(destination.Data))
            return new IccPipeline([], sourceModel.ChannelCount, destinationModel.ChannelCount, isIdentity: true);

        var stages = new IccStageList();
        sourceModel.AppendToConnectionSpace(stages, intent);
        if (intent == IccRenderingIntent.AbsoluteColorimetric)
        {
            // ICC.1:2022 section 6.3.2: the colorimetry relative to the media white of the source is rescaled to the media
            // white of the destination, component by component (the illuminant of the connection space cancels out)
            var sourceWhite = sourceModel.GetMediaWhitePoint();
            var destinationWhite = destinationModel.GetMediaWhitePoint();
            stages.Add(IccMatrixStage.CreateScale(sourceWhite.X / destinationWhite.X, sourceWhite.Y / destinationWhite.Y, sourceWhite.Z / destinationWhite.Z));
        }
        else if (blackPointCompensation && IccBlackPoint.CreateCompensation(sourceModel, destinationModel, intent) is { } compensation)
        {
            stages.Add(compensation);
        }

        destinationModel.AppendFromConnectionSpace(stages, intent);
        return new IccPipeline(stages.ToArray(), sourceModel.ChannelCount, destinationModel.ChannelCount, isIdentity: false);
    }

    /// <summary>Creates the conversion from a CICP color space to a profile.</summary>
    /// <remarks>
    /// The source is treated as a display: its white is the white of the connection space once adapted, so the
    /// ICC-absolute colorimetric intent rescales by the media white of the destination only, and its black is no light
    /// (every transfer function decodes its lowest sample to none), which is what black point compensation maps to the
    /// black of the destination.
    /// </remarks>
    /// <param name="source">The color space of the source samples.</param>
    /// <param name="destination">The profile describing the converted samples.</param>
    /// <param name="intent">The rendering intent, which selects the conversion of the destination.</param>
    /// <param name="blackPointCompensation">Whether the black of the source is mapped to the black of the destination.</param>
    /// <param name="toneMapping">The stage that brings the light of high dynamic range samples to [0, 1], or <see langword="null"/> for samples of a standard dynamic range.</param>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is valid but cannot be a destination, or the primaries do not define a color space.</exception>
    public static IccPipeline Create(CicpColorSpace source, IccProfile destination, IccRenderingIntent intent, bool blackPointCompensation, HdrToneMapStage? toneMapping = null)
    {
        var destinationModel = IccProfileModel.Create(destination, "destination");
        var stages = new IccStageList();
        stages.Add(new CicpDecodeStage(source.Transfer, source.ChannelCount));
        if (toneMapping is not null)
            stages.Add(toneMapping);

        if (source.IsGray)
        {
            stages.Add(new IccGrayExpandStage(IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z));
        }
        else
        {
            Span<double> matrix = stackalloc double[9];
            if (!source.TryGetToConnectionSpace(matrix))
                throw new UnsupportedImageFeatureException($"The color primaries {source.Primaries} of the CICP color description do not define a color space.", ImageFormat.Unknown, "CICP color primaries without a color space");

            stages.Add(new IccMatrixStage(matrix));
        }

        if (intent == IccRenderingIntent.AbsoluteColorimetric)
        {
            var destinationWhite = destinationModel.GetMediaWhitePoint();
            stages.Add(IccMatrixStage.CreateScale(IccColorimetry.D50X / destinationWhite.X, IccColorimetry.D50Y / destinationWhite.Y, IccColorimetry.D50Z / destinationWhite.Z));
        }
        else if (blackPointCompensation && IccBlackPoint.CreateCompensation(sourceLightness: 0, destinationModel, intent) is { } compensation)
        {
            stages.Add(compensation);
        }

        destinationModel.AppendFromConnectionSpace(stages, intent);
        return new IccPipeline(stages.ToArray(), source.ChannelCount, destinationModel.ChannelCount, isIdentity: false);
    }

    /// <summary>Creates the conversion from a profile to a CICP color space.</summary>
    /// <remarks>
    /// The destination is treated as a display, as a CICP source is: the white of the connection space is its white once
    /// adapted (the inverse of the matrix of <see cref="CicpColorSpace.TryGetToConnectionSpace"/>), and its black is no
    /// light, which is what black point compensation maps the black of the source to. A color outside the color space
    /// is clipped component by component, by the function that encodes it.
    /// </remarks>
    /// <param name="source">The profile describing the source samples.</param>
    /// <param name="destination">The color space of the converted samples, of a standard dynamic range.</param>
    /// <param name="intent">The rendering intent, which selects the conversion of the source.</param>
    /// <param name="blackPointCompensation">Whether the black of the source is mapped to the black of the destination.</param>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is valid but not supported, or the primaries do not define a color space.</exception>
    public static IccPipeline Create(IccProfile source, CicpColorSpace destination, IccRenderingIntent intent, bool blackPointCompensation)
    {
        var sourceModel = IccProfileModel.Create(source, "source");
        var stages = new IccStageList();
        sourceModel.AppendToConnectionSpace(stages, intent);
        if (intent == IccRenderingIntent.AbsoluteColorimetric)
        {
            var sourceWhite = sourceModel.GetMediaWhitePoint();
            stages.Add(IccMatrixStage.CreateScale(sourceWhite.X / IccColorimetry.D50X, sourceWhite.Y / IccColorimetry.D50Y, sourceWhite.Z / IccColorimetry.D50Z));
        }
        else if (blackPointCompensation && IccBlackPoint.CreateCompensationToBlack(sourceModel, intent) is { } compensation)
        {
            stages.Add(compensation);
        }

        if (destination.IsGray)
        {
            // The gray level is the fraction of the white: Y of the connection space
            stages.Add(new IccGrayReduceStage(index: 1, scale: 1));
        }
        else
        {
            Span<double> matrix = stackalloc double[9];
            Span<double> inverse = stackalloc double[9];
            if (!destination.TryGetToConnectionSpace(matrix) || !IccMatrixStage.TryInvert(matrix, inverse))
                throw new UnsupportedImageFeatureException($"The color primaries {destination.Primaries} of the CICP color description do not define a color space.", ImageFormat.Unknown, "CICP color primaries without a color space");

            stages.Add(new IccMatrixStage(inverse));
        }

        stages.Add(new CicpEncodeStage(destination.Transfer, destination.ChannelCount));
        return new IccPipeline(stages.ToArray(), sourceModel.ChannelCount, destination.ChannelCount, isIdentity: false);
    }

    /// <summary>Converts one color in place.</summary>
    /// <param name="values">
    /// At least <see cref="MaxChannels"/> elements: the normalized source device values on input (clipped to [0, 1] by
    /// the first stage), the normalized destination device values in [0, 1] on output.
    /// </param>
    public void Apply(Span<double> values)
    {
        foreach (var stage in _stages)
        {
            stage.Apply(values);
        }
    }

    /// <summary>
    /// Gets, when the last stage is one inverse tone curve per destination channel, the tables that give the 8-bit
    /// destination sample of each channel from the value before that stage (<see cref="ApplyBetweenCurves"/>): the same
    /// sample as evaluating the curve then <see cref="IccByteSample.Store"/>. The tables are built once the pipeline has
    /// converted <see cref="ByteOutputTablesThreshold"/> colors, counting the colors of this call.
    /// </summary>
    /// <param name="colors">The number of colors the caller is about to convert.</param>
    /// <returns>The table of each destination channel, or <see langword="null"/> when the last stage is evaluated.</returns>
    public QuantizationThresholds[]? GetByteOutputTables(int colors)
    {
        if (_outputCurves is not { } curves)
            return null;

        var tables = Volatile.Read(ref _byteOutputTables);
        if (tables is null)
        {
            if (Interlocked.Add(ref _outputCurveColors, colors) < ByteOutputTablesThreshold)
                return null;

            tables = CreateByteOutputTables(curves);
            tables = Interlocked.CompareExchange(ref _byteOutputTables, tables, null) ?? tables;
        }

        return ReferenceEquals(tables, NoByteOutputTables) ? null : tables;
    }

    // A table per channel when every curve matches its thresholds, NoByteOutputTables otherwise
    private static QuantizationThresholds[] CreateByteOutputTables(IccInverseCurve[] curves)
    {
        var tables = new QuantizationThresholds[curves.Length];
        for (var channel = 0; channel < tables.Length; channel++)
        {
            // The channels whose curve is the same curve (the same tag of the profile) share its table
            var curve = curves[channel];
            var shared = Array.FindIndex(curves, 0, channel, other => ReferenceEquals(other, curve));
            if (shared >= 0)
            {
                tables[channel] = tables[shared];
                continue;
            }

            if (QuantizationThresholds.Create(value => IccByteSample.Store(curve.Evaluate(value)), byte.MaxValue) is not { } table)
                return NoByteOutputTables;

            tables[channel] = table;
        }

        return tables;
    }

    /// <summary>
    /// Evaluates the first stage on one normalized sample of a source channel, as the stage does, when it is one curve per
    /// source channel (<see cref="HasInputCurves"/>).
    /// </summary>
    public double EvaluateInputCurve(int channel, double value) => _inputDecode is { } decode ? decode.Evaluate(value) : _inputCurves![channel].Evaluate(value);

    /// <summary>Determines whether two source channels have the same input curve (the same function, so the same table), when <see cref="HasInputCurves"/>.</summary>
    public bool HaveSameInputCurve(int channel, int other) => _inputDecode is not null || ReferenceEquals(_inputCurves![channel], _inputCurves[other]);

    /// <summary>
    /// Applies the stages from <paramref name="first"/> to <paramref name="end"/> (excluded) to colors stored by component
    /// (<see cref="IccStage.ApplyBatch"/>), with the results of <see cref="Apply"/> for each color.
    /// </summary>
    public void ApplyBatch(Span<double> components, int count, int first, int end)
    {
        for (var i = first; i < end; i++)
        {
            _stages[i].ApplyBatch(components, count);
        }
    }

    /// <summary>Converts one color in place, starting after the first stage: the values come from <see cref="ByteInputTables"/>.</summary>
    /// <param name="values">At least <see cref="MaxChannels"/> elements: the output of the first stage on input, the normalized destination device values on output.</param>
    public void ApplyAfterInputCurves(Span<double> values)
    {
        for (var i = 1; i < _stages.Length; i++)
        {
            _stages[i].Apply(values);
        }
    }

    /// <summary>
    /// Converts one color in place, from the values of <see cref="ByteInputTables"/> to the values before the last stage,
    /// whose 8-bit samples come from <see cref="GetByteOutputTables"/>.
    /// </summary>
    /// <param name="values">At least <see cref="MaxChannels"/> elements: the output of the first stage on input, the input of the last stage on output.</param>
    public void ApplyBetweenCurves(Span<double> values)
    {
        for (var i = 1; i < _stages.Length - 1; i++)
        {
            _stages[i].Apply(values);
        }
    }
}
