namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The description of an HEVC stream of an HEIC file for the container: the values that its parameter sets signal, and the
/// parameter sets themselves, which the configuration record of the stream holds (the coded pictures do not repeat them).
/// </summary>
internal sealed class HeicStreamDescription : HeifStreamDescription
{
    private readonly byte[] _videoParameterSet;
    private readonly byte[] _sequenceParameterSet;
    private readonly byte[] _pictureParameterSet;

    /// <summary>Creates the description of a stream.</summary>
    /// <param name="parameters">What the parameter sets signal.</param>
    /// <param name="videoParameterSet">The NAL unit of the video parameter set, with its header.</param>
    /// <param name="sequenceParameterSet">The NAL unit of the sequence parameter set, with its header.</param>
    /// <param name="pictureParameterSet">The NAL unit of the picture parameter set, with its header.</param>
    public HeicStreamDescription(in HevcSequenceParameters parameters, ReadOnlySpan<byte> videoParameterSet, ReadOnlySpan<byte> sequenceParameterSet, ReadOnlySpan<byte> pictureParameterSet)
    {
        Parameters = parameters;
        _videoParameterSet = videoParameterSet.ToArray();
        _sequenceParameterSet = sequenceParameterSet.ToArray();
        _pictureParameterSet = pictureParameterSet.ToArray();
    }

    /// <summary>Gets what the parameter sets of the stream signal: its profile selects the brands of the file.</summary>
    public HevcSequenceParameters Parameters { get; }

    public override int PlaneCount => Parameters.ChromaFormatIdc is 0 ? 1 : 3;

    public override int BitDepth => Parameters.BitDepth;

    // The conformance window is in units of chroma samples (ITU-T H.265, clause 7.4.3.2.1)
    public override Size? OutputSize => Parameters.OutputWidth == Parameters.Width && Parameters.OutputHeight == Parameters.Height
        ? null
        : new Size(Parameters.OutputWidth, Parameters.OutputHeight);

    /// <summary>
    /// Gets a value indicating whether the stream has the profile of the <c>heic</c> and <c>hevc</c> brands (ISO/IEC
    /// 23008-12, annex B.4): Main or Main Still Picture. The streams of the other profiles are those of the <c>heix</c> and
    /// <c>hevx</c> brands.
    /// </summary>
    public bool HasMainProfile => Parameters.GeneralProfileIdc is 1 or 3;

    public override void WriteConfigurationBox(HeifBoxWriter writer) => HeicConfigurationRecord.Write(writer, Parameters, _videoParameterSet, _sequenceParameterSet, _pictureParameterSet);
}
