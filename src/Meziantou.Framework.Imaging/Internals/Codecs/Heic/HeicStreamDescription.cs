using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The description of an HEVC stream of an HEIC file for the container: the values that its parameter sets signal, and the
/// parameter sets themselves, which the configuration record of the stream holds (the coded pictures do not repeat them).
/// </summary>
/// <remarks>
/// The level of the stream depends on the size of its coded pictures (<see cref="HevcLevels"/>), which the encoder only
/// knows once they are coded: the description raises the level of its parameter sets as the pictures are recorded. The
/// level is in the profile, tier and level syntax of the video and sequence parameter sets, which nothing else depends
/// on: the coded pictures and the picture parameter set are the same whatever the level.
/// </remarks>
internal sealed class HeicStreamDescription : HeifStreamDescription
{
    private readonly AllocationScope _scope;
    private readonly byte[] _pictureParameterSet;
    private byte[] _videoParameterSet;
    private byte[] _sequenceParameterSet;

    /// <summary>Creates the description of a stream.</summary>
    /// <param name="scope">The memory of the operation, for the parameter sets that the description rewrites.</param>
    /// <param name="parameters">What the parameter sets signal.</param>
    /// <param name="videoParameterSet">The NAL unit of the video parameter set, with its header.</param>
    /// <param name="sequenceParameterSet">The NAL unit of the sequence parameter set, with its header.</param>
    /// <param name="pictureParameterSet">The NAL unit of the picture parameter set, with its header.</param>
    public HeicStreamDescription(AllocationScope scope, in HevcSequenceParameters parameters, ReadOnlySpan<byte> videoParameterSet, ReadOnlySpan<byte> sequenceParameterSet, ReadOnlySpan<byte> pictureParameterSet)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        Parameters = parameters;
        _videoParameterSet = videoParameterSet.ToArray();
        _sequenceParameterSet = sequenceParameterSet.ToArray();
        _pictureParameterSet = pictureParameterSet.ToArray();
    }

    /// <summary>
    /// Gets what the parameter sets of the stream signal: its profile selects the brands of the file, and its level holds
    /// the pictures recorded so far.
    /// </summary>
    public HevcSequenceParameters Parameters { get; private set; }

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

    public override void RecordCodedPicture(int length)
    {
        // The data of a picture are its NAL unit, preceded by its length. A decoder is given that NAL unit, and the
        // parameter sets of the configuration record when it starts with it: their sum is at least the number of bytes of
        // any access unit
        var pictureLength = (long)length - HevcNalWriter.NalLengthSize + _videoParameterSet.Length + _sequenceParameterSet.Length + _pictureParameterSet.Length;
        var current = Parameters;
        if (pictureLength <= current.MaxPictureLength)
            return;

        var parameters = current with { MaxPictureLength = pictureLength };
        if (parameters.GeneralLevelIdc != current.GeneralLevelIdc || parameters.GeneralTierFlag != current.GeneralTierFlag)
        {
            // general_tier_flag is in the first byte of the profile, tier and level syntax, which no zero byte precedes,
            // and general_level_idc is a byte of at least 30: whatever their values, the same emulation prevention bytes
            // are inserted, and the configuration record keeps its size
            var videoParameterSet = WriteParameterSet(HevcConstants.NalVps, parameters);
            var sequenceParameterSet = WriteParameterSet(HevcConstants.NalSps, parameters);
            Debug.Assert(videoParameterSet.Length == _videoParameterSet.Length && sequenceParameterSet.Length == _sequenceParameterSet.Length);
            _videoParameterSet = videoParameterSet;
            _sequenceParameterSet = sequenceParameterSet;
        }

        Parameters = parameters;
    }

    public override void WriteConfigurationBox(HeifBoxWriter writer) => HeicConfigurationRecord.Write(writer, Parameters, _videoParameterSet, _sequenceParameterSet, _pictureParameterSet);

    // The NAL unit of a video or sequence parameter set, as the encoder writes it (HevcEncoder)
    private byte[] WriteParameterSet(int nalUnitType, in HevcSequenceParameters parameters)
    {
        using var rbsp = new PooledPayloadWriter(_scope);
        using var nalUnit = new PooledPayloadWriter(_scope);
        if (nalUnitType is HevcConstants.NalVps)
        {
            HevcHeaderWriter.WriteVideoParameterSet(rbsp, parameters);
        }
        else
        {
            HevcHeaderWriter.WriteSequenceParameterSet(rbsp, parameters);
        }

        HevcNalWriter.WriteNalUnit(nalUnit, nalUnitType, 0, rbsp.WrittenSpan);
        return nalUnit.WrittenSpan.ToArray();
    }
}
