using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The HEIC format in the HEIF container layer: images coded with HEVC (ISO/IEC 23008-12, annex B, and ISO/IEC 14496-15,
/// section 8 for the configuration record and the format of the samples).
/// </summary>
internal sealed class HeicBinding : HeifCodecBinding
{
    private HeicBinding()
    {
    }

    public static HeicBinding Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Heic;

    public override string Name => "HEIC";

    public override string CodecName => "HEVC";

    public override uint ConfigurationBoxType => HeifFormat.HvcC;

    public override uint CodedItemType => HeifFormat.Hvc1;

    public override uint SampleEntryType => HeifFormat.Hvc1;

    public override ReadOnlySpan<byte> AlphaAuxiliaryTypeToWrite => HeifFormat.HevcAlphaAuxiliaryType;

    // ITU-T H.265, annex A.4.2: a decoded picture buffer holds at most 16 pictures, so at most 15 pictures precede in
    // decoding order a picture that they follow in output order
    public override int MaxReorderedSamples => 16;

    // The parameter sets of the bitstream are in the record
    public override bool NeedsConfigurationRecord => true;

    // ITU-T H.265, clause 6.2 and figure E.1: the chroma samples of 4:2:2 pictures, and of 4:2:0 pictures of the default
    // chroma sample location type, are in the columns of luma samples
    public override bool HasCositedChromaColumns => true;

    /// <summary>Returns whether a brand is one of the HEVC brands of HEIF, for an image or for an image sequence.</summary>
    public static bool IsBrand(uint brand) => brand is HeifFormat.Heic or HeifFormat.Heix or HeifFormat.Heim or HeifFormat.Heis or HeifFormat.Hevc or HeifFormat.Hevx or HeifFormat.Hevm or HeifFormat.Hevs;

    public override bool IsImageBrand(uint brand) => brand is HeifFormat.Heic or HeifFormat.Heix or HeifFormat.Heim or HeifFormat.Heis;

    public override bool IsSequenceBrand(uint brand) => brand is HeifFormat.Hevc or HeifFormat.Hevx or HeifFormat.Hevm or HeifFormat.Hevs;

    public override bool IsCodedItemType(uint type) => type is HeifFormat.Hvc1;

    // The parameter sets of an 'hev1' sample entry may also be in the samples, which the decoder reads either way
    public override bool IsSampleEntryType(uint type) => type is HeifFormat.Hvc1 or HeifFormat.Hev1;

    // ISO/IEC 23008-12, annex B.2.2 for HEVC auxiliary images; writers of MIAF files also use the codec-independent type
    public override bool IsAlphaAuxiliaryType(ReadOnlySpan<byte> auxiliaryType)
        => auxiliaryType.SequenceEqual(HeifFormat.HevcAlphaAuxiliaryType) || auxiliaryType.SequenceEqual(HeifFormat.AlphaAuxiliaryType);

    public override PixelFormat GetDefaultPixelFormat(bool isMonochrome, bool hasAlpha, int bitDepth) => DefaultPixelFormats.ForHeic(isMonochrome, hasAlpha, bitDepth);

    public override HeifSampleFormat ReadConfigurationRecord(ref HeifBoxReader reader) => HeicConfigurationRecord.Read(this, ref reader);

    public override bool TryReadItemProperty(uint type, ref HeifBoxReader reader, bool isCodedItem, HeifCodedImage image)
    {
        if (type is not HeifFormat.Lsel)
            return false;

        // 0xFFFF: every layer may be rendered while the data arrive, and the last one is the image. The base layer is the
        // only layer of the HEVC images that the decoder reads
        var layer = reader.ReadUInt16();
        if (layer is not (0 or 0xFFFF))
            throw Unsupported("The HEIC image selects a layer of a layered HEVC bitstream.", "layered image");

        return true;
    }

    // The sequence parameter set is the header of the bitstream: it is in the configuration record, or else (an 'hev1'
    // sample entry allows it) before the first slice of the data, whose NAL units are preceded by their length
    public override bool? TryLocateBitstreamHeader(ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> data, out int end)
    {
        end = 0;
        if (configuration.IsEmpty)
            return false;

        if (TryFindSequenceParameterSet(configuration, out _))
            return true;

        var lengthSize = HeicConfigurationRecord.GetNalUnitLengthSize(configuration);
        var position = 0;
        while (true)
        {
            if (data.Length - position < lengthSize + 2)
                return null;

            var length = ReadNalUnitLength(data[position..], lengthSize);
            if (length < 2)
                return false;

            var type = GetNalUnitType(data[(position + lengthSize)..]);
            var next = (long)position + lengthSize + length;
            if (type is HevcConstants.NalSps)
            {
                if (next > data.Length)
                    return null;

                end = (int)next;
                return true;
            }

            // A parameter set precedes the slices that refer to it
            if (type <= HevcConstants.NalRsvVcl31 || next > int.MaxValue)
                return false;

            position = (int)next;
        }
    }

    public override HeifBitstreamFormat ReadBitstreamFormat(AllocationScope scope, ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> data, int operatingPoint)
    {
        if (!TryFindSequenceParameterSet(configuration, out var nalUnit))
        {
            var lengthSize = configuration.IsEmpty ? 4 : HeicConfigurationRecord.GetNalUnitLengthSize(configuration);
            var position = 0;
            while (nalUnit.IsEmpty)
            {
                if (data.Length - position < lengthSize + 2)
                    throw Invalid("an HEVC image has no sequence parameter set.");

                var length = ReadNalUnitLength(data[position..], lengthSize);
                if (length < 2 || length > data.Length - position - lengthSize)
                    throw Invalid("a NAL unit of an HEVC image exceeds its data.");

                var unit = data.Slice(position + lengthSize, (int)length);
                var type = GetNalUnitType(unit);
                if (type is HevcConstants.NalSps)
                {
                    nalUnit = unit;
                }
                else if (type <= HevcConstants.NalRsvVcl31)
                {
                    throw Invalid("an HEVC image has no sequence parameter set.");
                }

                position += lengthSize + (int)length;
            }
        }

        return ToBitstreamFormat(HevcSequenceParameterSet.Parse(scope, nalUnit));
    }

    public override HeifDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new HeicDecoder(request, context);

    // ISO/IEC 23008-12, annex B.4: 'heic' and 'hevc' are the brands of the files whose images have the Main or the Main
    // Still Picture profile, 'heix' and 'hevx' those of the other profiles. The color images decide: the alpha plane, a
    // monochrome image that has a profile of the format range extensions, is an auxiliary image, and the files of Apple
    // and of libheif that have one keep the 'heic' brand
    public override void WriteFileTypeBrands(HeifBoxWriter writer, bool isSequence, HeifStreamDescription color, HeifStreamDescription? alpha)
    {
        var isMain = ((HeicStreamDescription)color).HasMainProfile;
        var imageBrand = isMain ? HeifFormat.Heic : HeifFormat.Heix;
        var sequenceBrand = isMain ? HeifFormat.Hevc : HeifFormat.Hevx;
        writer.WriteUInt32(isSequence ? sequenceBrand : imageBrand);
        writer.WriteUInt32(0);
        writer.WriteUInt32(imageBrand);
        if (isSequence)
        {
            writer.WriteUInt32(sequenceBrand);
            writer.WriteUInt32(HeifFormat.Msf1);
            writer.WriteUInt32(HeifFormat.Iso8);
        }

        writer.WriteUInt32(HeifFormat.Mif1);
        writer.WriteUInt32(HeifFormat.Miaf);
    }

    public override HeifPictureEncoder CreatePictureEncoder(AllocationScope scope, in HeifPictureSettings settings) => new HeicPictureEncoder(scope, settings);

    /// <summary>Gets the sample format and the color description of a sequence parameter set.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The chroma samples are at a position that the conversion to pixels does not support.</exception>
    public static HeifBitstreamFormat ToBitstreamFormat(HevcSequenceParameterSet sps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        var vui = sps.Vui;
        var isMonochrome = sps.ChromaFormatIdc is 0;
        var subsamplingX = sps.SubWidthC is 2 ? 1 : 0;
        var subsamplingY = sps.SubHeightC is 2 ? 1 : 0;

        // ITU-T H.265, figure E.1: the chroma samples of a 4:2:0 picture are at the left of their luma samples and between
        // two rows of them (type 0, the default), in the middle of four luma samples (type 1), or at the position of the
        // top-left one (type 2). The other types (above the middle, at the bottom-left one, below the middle) have no
        // conversion here. The chroma samples of a 4:2:2 picture are at the position of the luma samples of even columns
        // (clause 6.2)
        var chromaSamplePosition = Av1Constants.CspUnknown;
        if (subsamplingX is 1 && subsamplingY is 0)
        {
            chromaSamplePosition = Av1Constants.CspVertical;
        }
        else if (subsamplingY is 1)
        {
            chromaSamplePosition = (vui is { ChromaLocInfoPresentFlag: true } ? vui.ChromaSampleLocTypeTopField : 0) switch
            {
                0 => Av1Constants.CspVertical,
                1 => Av1Constants.CspUnknown,
                2 => Av1Constants.CspColocated,
                var type => throw Instance.Unsupported($"The chroma sample location type {type} of the HEIC image is not supported.", "chroma sample location"),
            };
        }

        // ITU-T H.265, clause E.3.1: without video signal description, video_full_range_flag is inferred to be 0 (the
        // limited range), which is how the HEVC decoders read such a stream, and not the full range that MIAF gives to
        // an image without color information; without color description, the matrix coefficients are unspecified
        var hasVideoSignal = vui is { VideoSignalTypePresentFlag: true };
        var hasColorDescription = vui is { VideoSignalTypePresentFlag: true, ColourDescriptionPresentFlag: true };
        return new HeifBitstreamFormat(
            sps.BitDepth,
            isMonochrome,
            subsamplingX,
            subsamplingY,
            chromaSamplePosition,
            hasColorDescription ? vui!.MatrixCoeffs : Av1Constants.McUnspecified,
            hasVideoSignal && vui!.VideoFullRangeFlag,
            hasColorDescription,
            hasColorDescription ? vui!.ColourPrimaries : Av1Constants.CpUnspecified,
            hasColorDescription ? vui!.TransferCharacteristics : Av1Constants.TcUnspecified)
        {
            IsRangeSignaled = hasVideoSignal,
        };
    }

    private static bool TryFindSequenceParameterSet(ReadOnlySpan<byte> configuration, out ReadOnlySpan<byte> nalUnit)
    {
        nalUnit = default;
        if (configuration.IsEmpty)
            return false;

        var position = default(HeicConfigurationRecord.Position);
        while (HeicConfigurationRecord.TryReadNalUnit(configuration, ref position, out var unit))
        {
            if (GetNalUnitType(unit) is HevcConstants.NalSps)
            {
                nalUnit = unit;
                return true;
            }
        }

        return false;
    }

    private static uint ReadNalUnitLength(ReadOnlySpan<byte> data, int lengthSize) => lengthSize switch
    {
        1 => data[0],
        2 => BinaryPrimitives.ReadUInt16BigEndian(data),
        _ => BinaryPrimitives.ReadUInt32BigEndian(data),
    };

    // ITU-T H.265, clause 7.3.1.2: forbidden_zero_bit, then the six bits of nal_unit_type
    private static int GetNalUnitType(ReadOnlySpan<byte> nalUnit) => (nalUnit[0] >> 1) & 0x3F;
}
