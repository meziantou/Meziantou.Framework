using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An ICC color conversion between two profiles: the ordered stages converting the normalized device values of the
/// source profile to those of the destination profile, through the profile connection space. Immutable and thread-safe.
/// </summary>
internal sealed class IccPipeline
{
    /// <summary>The largest number of components a stage reads or writes.</summary>
    public const int MaxChannels = 4;

    private readonly IccStage[] _stages;

    private IccPipeline(IccStage[] stages, int sourceChannelCount, int destinationChannelCount, bool isIdentity)
    {
        _stages = stages;
        SourceChannelCount = sourceChannelCount;
        DestinationChannelCount = destinationChannelCount;
        IsIdentity = isIdentity;

        // The first stage of most conversions is one curve per source channel (tone curves, or the input tables of a
        // lookup table): its values for the 256 possible 8-bit samples are computed once, by the same evaluation as
        // IccCurvesStage on the same normalized sample, so that using the table gives bit-identical results
        if (stages.Length > 0 && stages[0] is IccCurvesStage { Curves: var curves } && curves.Length == sourceChannelCount)
        {
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
    }

    /// <summary>
    /// Gets, when the first stage is one curve per source channel, the value of that stage for each 8-bit sample of each
    /// channel; <see langword="null"/> otherwise. See <see cref="ApplyAfterInputCurves"/>.
    /// </summary>
    public double[][]? ByteInputTables { get; }

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

    /// <summary>Converts one color in place, starting after the first stage: the values come from <see cref="ByteInputTables"/>.</summary>
    /// <param name="values">At least <see cref="MaxChannels"/> elements: the output of the first stage on input, the normalized destination device values on output.</param>
    public void ApplyAfterInputCurves(Span<double> values)
    {
        for (var i = 1; i < _stages.Length; i++)
        {
            _stages[i].Apply(values);
        }
    }
}
