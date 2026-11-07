namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sequence header of an AV1 coded video sequence (AV1 specification, section 5.5): the profile, the maximum frame size, the
/// sample format, the color description and the coding tools that the frames of the sequence may use. Immutable once parsed.
/// </summary>
internal sealed class Av1SequenceHeader
{
    private Av1OperatingPoint[] _operatingPoints = [];

    private Av1SequenceHeader()
    {
    }

    public int Profile { get; private set; }

    public bool StillPicture { get; private set; }

    public bool ReducedStillPictureHeader { get; private set; }

    public bool TimingInfoPresent { get; private set; }

    public bool EqualPictureInterval { get; private set; }

    public bool DecoderModelInfoPresent { get; private set; }

    public int BufferDelayLength { get; private set; }

    public int BufferRemovalTimeLength { get; private set; }

    public int FramePresentationTimeLength { get; private set; }

    /// <summary>Gets the number of operating points (<c>operating_points_cnt_minus_1 + 1</c>).</summary>
    public int OperatingPointCount { get; private set; }

    /// <summary>Gets the layers of the selected operating point (<c>OperatingPointIdc</c>): temporal layers in bits 0-7, spatial layers in bits 8-11.</summary>
    public int OperatingPointIdc { get; private set; }

    public int FrameWidthBits { get; private set; }

    public int FrameHeightBits { get; private set; }

    public int MaxFrameWidth { get; private set; }

    public int MaxFrameHeight { get; private set; }

    public bool FrameIdNumbersPresent { get; private set; }

    public int DeltaFrameIdLength { get; private set; }

    /// <summary>Gets the number of bits of a frame identifier (<c>idLen</c>), 0 when identifiers are absent.</summary>
    public int FrameIdLength { get; private set; }

    public bool Use128x128Superblock { get; private set; }

    public bool EnableFilterIntra { get; private set; }

    public bool EnableIntraEdgeFilter { get; private set; }

    public bool EnableInterIntraCompound { get; private set; }

    public bool EnableMaskedCompound { get; private set; }

    public bool EnableWarpedMotion { get; private set; }

    public bool EnableDualFilter { get; private set; }

    public bool EnableOrderHint { get; private set; }

    public bool EnableJntComp { get; private set; }

    public bool EnableRefFrameMvs { get; private set; }

    /// <summary>Gets <c>seq_force_screen_content_tools</c>: 0, 1 or <see cref="Av1Constants.SelectScreenContentTools"/>.</summary>
    public int ForceScreenContentTools { get; private set; }

    /// <summary>Gets <c>seq_force_integer_mv</c>: 0, 1 or <see cref="Av1Constants.SelectIntegerMv"/>.</summary>
    public int ForceIntegerMv { get; private set; }

    public int OrderHintBits { get; private set; }

    public bool EnableSuperres { get; private set; }

    public bool EnableCdef { get; private set; }

    public bool EnableRestoration { get; private set; }

    public int BitDepth { get; private set; }

    public bool Monochrome { get; private set; }

    /// <summary>Gets the number of planes: 1 for monochrome, otherwise 3.</summary>
    public int PlaneCount => Monochrome ? 1 : 3;

    public bool ColorDescriptionPresent { get; private set; }

    public int ColorPrimaries { get; private set; }

    public int TransferCharacteristics { get; private set; }

    public int MatrixCoefficients { get; private set; }

    /// <summary>Gets <c>color_range</c>: <see langword="true"/> for full range samples.</summary>
    public bool FullRange { get; private set; }

    public int SubsamplingX { get; private set; }

    public int SubsamplingY { get; private set; }

    public int ChromaSamplePosition { get; private set; }

    public bool SeparateUvDeltaQ { get; private set; }

    public bool FilmGrainParamsPresent { get; private set; }

    public Av1OperatingPoint GetOperatingPoint(int index) => _operatingPoints[index];

    /// <summary>
    /// Parses the payload of a sequence header OBU for the operating point <paramref name="operatingPoint"/>
    /// (<c>choose_operating_point()</c>).
    /// </summary>
    /// <exception cref="InvalidImageContentException">The header is malformed, or the operating point does not exist.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The header uses a reserved profile.</exception>
    public static Av1SequenceHeader Parse(ReadOnlySpan<byte> payload, int operatingPoint)
    {
        var reader = new Av1BitReader(payload);
        var header = new Av1SequenceHeader();
        header.Profile = (int)reader.ReadBits(3);
        if (header.Profile > 2)
            throw Av1Errors.Unsupported("reserved profile");

        header.StillPicture = reader.ReadFlag();
        header.ReducedStillPictureHeader = reader.ReadFlag();
        if (header.ReducedStillPictureHeader)
        {
            if (!header.StillPicture)
                throw Av1Errors.Invalid("a reduced still picture header requires a still picture.");

            header.OperatingPointCount = 1;
            header._operatingPoints = [new Av1OperatingPoint(Idc: 0, LevelIndex: (int)reader.ReadBits(5), Tier: 0, DecoderModelPresent: false)];
        }
        else
        {
            header.TimingInfoPresent = reader.ReadFlag();
            if (header.TimingInfoPresent)
            {
                reader.ReadBits(32); // num_units_in_display_tick
                reader.ReadBits(32); // time_scale
                header.EqualPictureInterval = reader.ReadFlag();
                if (header.EqualPictureInterval)
                    reader.ReadUvlc(); // num_ticks_per_picture_minus_1

                header.DecoderModelInfoPresent = reader.ReadFlag();
                if (header.DecoderModelInfoPresent)
                {
                    header.BufferDelayLength = (int)reader.ReadBits(5) + 1;
                    reader.ReadBits(32); // num_units_in_decoding_tick
                    header.BufferRemovalTimeLength = (int)reader.ReadBits(5) + 1;
                    header.FramePresentationTimeLength = (int)reader.ReadBits(5) + 1;
                }
            }

            var initialDisplayDelayPresent = reader.ReadFlag();
            header.OperatingPointCount = (int)reader.ReadBits(5) + 1;
            header._operatingPoints = new Av1OperatingPoint[header.OperatingPointCount];
            for (var i = 0; i < header.OperatingPointCount; i++)
            {
                var idc = (int)reader.ReadBits(12);
                var levelIndex = (int)reader.ReadBits(5);
                var tier = levelIndex > 7 ? (int)reader.ReadBits(1) : 0;
                var decoderModelPresent = false;
                if (header.DecoderModelInfoPresent)
                {
                    decoderModelPresent = reader.ReadFlag();
                    if (decoderModelPresent)
                    {
                        reader.ReadBits(header.BufferDelayLength); // decoder_buffer_delay
                        reader.ReadBits(header.BufferDelayLength); // encoder_buffer_delay
                        reader.ReadBits(1); // low_delay_mode_flag
                    }
                }

                if (initialDisplayDelayPresent && reader.ReadFlag())
                    reader.ReadBits(4); // initial_display_delay_minus_1

                header._operatingPoints[i] = new Av1OperatingPoint(idc, levelIndex, tier, decoderModelPresent);
            }
        }

        if ((uint)operatingPoint >= (uint)header.OperatingPointCount)
            throw Av1Errors.Invalid("the selected operating point does not exist.");

        header.OperatingPointIdc = header._operatingPoints[operatingPoint].Idc;

        header.FrameWidthBits = (int)reader.ReadBits(4) + 1;
        header.FrameHeightBits = (int)reader.ReadBits(4) + 1;
        header.MaxFrameWidth = (int)reader.ReadBits(header.FrameWidthBits) + 1;
        header.MaxFrameHeight = (int)reader.ReadBits(header.FrameHeightBits) + 1;
        if (!header.ReducedStillPictureHeader)
            header.FrameIdNumbersPresent = reader.ReadFlag();

        if (header.FrameIdNumbersPresent)
        {
            header.DeltaFrameIdLength = (int)reader.ReadBits(4) + 2;
            header.FrameIdLength = (int)reader.ReadBits(3) + 1 + header.DeltaFrameIdLength;
            if (header.FrameIdLength > 16)
                throw Av1Errors.Invalid("frame identifiers use more than 16 bits.");
        }

        header.Use128x128Superblock = reader.ReadFlag();
        header.EnableFilterIntra = reader.ReadFlag();
        header.EnableIntraEdgeFilter = reader.ReadFlag();
        if (header.ReducedStillPictureHeader)
        {
            header.ForceScreenContentTools = Av1Constants.SelectScreenContentTools;
            header.ForceIntegerMv = Av1Constants.SelectIntegerMv;
        }
        else
        {
            header.EnableInterIntraCompound = reader.ReadFlag();
            header.EnableMaskedCompound = reader.ReadFlag();
            header.EnableWarpedMotion = reader.ReadFlag();
            header.EnableDualFilter = reader.ReadFlag();
            header.EnableOrderHint = reader.ReadFlag();
            if (header.EnableOrderHint)
            {
                header.EnableJntComp = reader.ReadFlag();
                header.EnableRefFrameMvs = reader.ReadFlag();
            }

            var chooseScreenContentTools = reader.ReadFlag();
            header.ForceScreenContentTools = chooseScreenContentTools ? Av1Constants.SelectScreenContentTools : (int)reader.ReadBits(1);
            if (header.ForceScreenContentTools > 0)
            {
                var chooseIntegerMv = reader.ReadFlag();
                header.ForceIntegerMv = chooseIntegerMv ? Av1Constants.SelectIntegerMv : (int)reader.ReadBits(1);
            }
            else
            {
                header.ForceIntegerMv = Av1Constants.SelectIntegerMv;
            }

            if (header.EnableOrderHint)
                header.OrderHintBits = (int)reader.ReadBits(3) + 1;
        }

        header.EnableSuperres = reader.ReadFlag();
        header.EnableCdef = reader.ReadFlag();
        header.EnableRestoration = reader.ReadFlag();
        header.ReadColorConfig(ref reader);
        header.FilmGrainParamsPresent = reader.ReadFlag();
        reader.ReadTrailingBits();
        return header;
    }

    // Section 5.5.2 "Color config syntax"
    private void ReadColorConfig(ref Av1BitReader reader)
    {
        var highBitDepth = reader.ReadFlag();
        if (Profile is 2 && highBitDepth)
        {
            BitDepth = reader.ReadFlag() ? 12 : 10;
        }
        else
        {
            BitDepth = highBitDepth ? 10 : 8;
        }

        Monochrome = Profile is not 1 && reader.ReadFlag();
        ColorDescriptionPresent = reader.ReadFlag();
        if (ColorDescriptionPresent)
        {
            ColorPrimaries = (int)reader.ReadBits(8);
            TransferCharacteristics = (int)reader.ReadBits(8);
            MatrixCoefficients = (int)reader.ReadBits(8);
        }
        else
        {
            ColorPrimaries = Av1Constants.CpUnspecified;
            TransferCharacteristics = Av1Constants.TcUnspecified;
            MatrixCoefficients = Av1Constants.McUnspecified;
        }

        if (Monochrome)
        {
            FullRange = reader.ReadFlag();
            SubsamplingX = 1;
            SubsamplingY = 1;
            ChromaSamplePosition = Av1Constants.CspUnknown;
            SeparateUvDeltaQ = false;
            return;
        }

        if (ColorPrimaries is Av1Constants.CpBt709 && TransferCharacteristics is Av1Constants.TcSrgb && MatrixCoefficients is Av1Constants.McIdentity)
        {
            FullRange = true;
            SubsamplingX = 0;
            SubsamplingY = 0;
        }
        else
        {
            FullRange = reader.ReadFlag();
            if (Profile is 0)
            {
                SubsamplingX = 1;
                SubsamplingY = 1;
            }
            else if (Profile is 1)
            {
                SubsamplingX = 0;
                SubsamplingY = 0;
            }
            else if (BitDepth is 12)
            {
                SubsamplingX = (int)reader.ReadBits(1);
                SubsamplingY = SubsamplingX is 1 ? (int)reader.ReadBits(1) : 0;
            }
            else
            {
                SubsamplingX = 1;
                SubsamplingY = 0;
            }

            if (SubsamplingX is 1 && SubsamplingY is 1)
                ChromaSamplePosition = (int)reader.ReadBits(2);
        }

        if (MatrixCoefficients is Av1Constants.McIdentity && (SubsamplingX is not 0 || SubsamplingY is not 0))
            throw Av1Errors.Invalid("the identity matrix requires 4:4:4 samples.");

        SeparateUvDeltaQ = reader.ReadFlag();
    }

    /// <summary>
    /// Returns whether <paramref name="other"/> describes the same coded video sequence: a sequence header may be repeated, and a
    /// different one starts a new sequence (section 7.5).
    /// </summary>
    /// <remarks>
    /// The comparison is on what the headers hold, whatever the operating point each was parsed for: the tiles of a grid may
    /// select different operating points of the same sequence.
    /// </remarks>
    public bool IsSameSequence(Av1SequenceHeader other)
    {
        for (var i = 0; i < Math.Min(OperatingPointCount, other.OperatingPointCount); i++)
        {
            if (_operatingPoints[i].Idc != other._operatingPoints[i].Idc)
                return false;
        }

        return Profile == other.Profile
            && StillPicture == other.StillPicture
            && ReducedStillPictureHeader == other.ReducedStillPictureHeader
            && OperatingPointCount == other.OperatingPointCount
            && FrameWidthBits == other.FrameWidthBits
            && FrameHeightBits == other.FrameHeightBits
            && MaxFrameWidth == other.MaxFrameWidth
            && MaxFrameHeight == other.MaxFrameHeight
            && FrameIdNumbersPresent == other.FrameIdNumbersPresent
            && DeltaFrameIdLength == other.DeltaFrameIdLength
            && FrameIdLength == other.FrameIdLength
            && Use128x128Superblock == other.Use128x128Superblock
            && EnableFilterIntra == other.EnableFilterIntra
            && EnableIntraEdgeFilter == other.EnableIntraEdgeFilter
            && EnableInterIntraCompound == other.EnableInterIntraCompound
            && EnableMaskedCompound == other.EnableMaskedCompound
            && EnableWarpedMotion == other.EnableWarpedMotion
            && EnableDualFilter == other.EnableDualFilter
            && EnableOrderHint == other.EnableOrderHint
            && EnableJntComp == other.EnableJntComp
            && EnableRefFrameMvs == other.EnableRefFrameMvs
            && ForceScreenContentTools == other.ForceScreenContentTools
            && ForceIntegerMv == other.ForceIntegerMv
            && OrderHintBits == other.OrderHintBits
            && EnableSuperres == other.EnableSuperres
            && EnableCdef == other.EnableCdef
            && EnableRestoration == other.EnableRestoration
            && BitDepth == other.BitDepth
            && Monochrome == other.Monochrome
            && ColorPrimaries == other.ColorPrimaries
            && TransferCharacteristics == other.TransferCharacteristics
            && MatrixCoefficients == other.MatrixCoefficients
            && FullRange == other.FullRange
            && SubsamplingX == other.SubsamplingX
            && SubsamplingY == other.SubsamplingY
            && ChromaSamplePosition == other.ChromaSamplePosition
            && SeparateUvDeltaQ == other.SeparateUvDeltaQ
            && FilmGrainParamsPresent == other.FilmGrainParamsPresent
            && TimingInfoPresent == other.TimingInfoPresent
            && EqualPictureInterval == other.EqualPictureInterval
            && DecoderModelInfoPresent == other.DecoderModelInfoPresent
            && BufferRemovalTimeLength == other.BufferRemovalTimeLength
            && FramePresentationTimeLength == other.FramePresentationTimeLength;
    }
}
