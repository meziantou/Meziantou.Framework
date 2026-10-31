namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The AVIF format in the HEIF container layer: images coded with AV1 (AV1 Image File Format, and AV1 Codec ISO Media File
/// Format Binding for the configuration record).
/// </summary>
internal sealed class AvifBinding : HeifCodecBinding
{
    private AvifBinding()
    {
    }

    public static AvifBinding Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Avif;

    public override string Name => "AVIF";

    public override string CodecName => "AV1";

    public override uint ConfigurationBoxType => HeifFormat.Av1C;

    public override HeifContainerFeatures ContainerFeatures => HeifContainerFeatures.PremultipliedAlpha | HeifContainerFeatures.DerivedImages | HeifContainerFeatures.EditLists | HeifContainerFeatures.Fragments | HeifContainerFeatures.ExtendedMatrices | HeifContainerFeatures.SequenceOrientation | HeifContainerFeatures.DisplayMetadata;

    public override bool IsImageBrand(uint brand) => brand is HeifFormat.Avif;

    public override bool IsSequenceBrand(uint brand) => brand is HeifFormat.Avis;

    public override bool IsCodedItemType(uint type) => type is HeifFormat.Av01;

    public override bool IsSampleEntryType(uint type) => type is HeifFormat.Av01;

    public override bool IsAlphaAuxiliaryType(ReadOnlySpan<byte> auxiliaryType) => auxiliaryType.SequenceEqual(HeifFormat.AlphaAuxiliaryType);

    public override PixelFormat GetDefaultPixelFormat(bool isMonochrome, bool hasAlpha, int bitDepth) => DefaultPixelFormats.ForAvif(isMonochrome, hasAlpha, bitDepth);

    // AV1 Codec ISO Media File Format Binding, section 2.3 "AV1 Codec Configuration Box"
    public override HeifSampleFormat ReadConfigurationRecord(ref HeifBoxReader reader)
    {
        if (reader.ReadByte() is not 0x81)
            throw Invalid("the AV1 configuration record is not valid.");

        reader.ReadByte();
        var flags = reader.ReadByte();
        var highBitDepth = (flags & 0x40) is not 0;
        var twelveBit = (flags & 0x20) is not 0;
        return new HeifSampleFormat(highBitDepth ? (twelveBit ? 12 : 10) : 8, (flags & 0x10) is not 0, (flags >> 3) & 1, (flags >> 2) & 1);
    }

    public override bool TryReadItemProperty(uint type, ref HeifBoxReader reader, bool isCodedItem, HeifCodedImage image)
    {
        switch (type)
        {
            case HeifFormat.A1op:
            {
                // The index is verified against the operating points of the sequence header when it is read
                var operatingPoint = reader.ReadByte();
                if (isCodedItem)
                    image.OperatingPoints[0] = operatingPoint;
                else if (operatingPoint is not 0)
                    throw Invalid("a derived image selects an operating point.");

                return true;
            }

            case HeifFormat.Lsel:
            {
                // 0xFFFF: every layer may be rendered while the data arrive, and the last one is the image
                var layer = reader.ReadUInt16();
                if (layer is 0xFFFF)
                    return true;

                if (layer > Av1ObuHeader.MaxSpatialId)
                    throw Invalid("an image selects a layer that AV1 does not have.");

                if (!isCodedItem)
                    throw Invalid("a derived image selects a layer.");

                image.Layers[0] = layer;
                return true;
            }

            case HeifFormat.A1lx:
                return true;
            default:
                return false;
        }
    }

    // Looks for the first sequence header in the first bytes of a bitstream: the configuration record of AV1 may repeat it,
    // but the bitstream always has it
    public override bool? TryLocateBitstreamHeader(ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> data, out int end)
    {
        end = 0;
        var offset = 0;
        while (offset < data.Length)
        {
            // AV1 specification, section 5.3.2 "OBU header syntax": only the last unit of the data may have no size field
            var header = data[offset];
            if ((header & 0x82) is not 0x02)
                return false;

            var position = offset + 1 + ((header >> 2) & 1);
            long size = 0;
            var shift = 0;
            while (true)
            {
                if (position >= data.Length)
                    return null;

                var value = data[position++];
                size |= (long)(value & 0x7F) << shift;
                if ((value & 0x80) is 0)
                    break;

                shift += 7;
                if (shift > 28)
                    return false;
            }

            var next = position + size;
            if (((header >> 3) & 15) is Av1Constants.ObuSequenceHeader)
            {
                if (next > data.Length)
                    return null;

                end = (int)next;
                return true;
            }

            if (next > int.MaxValue)
                return false;

            offset = (int)next;
        }

        return null;
    }

    public override HeifBitstreamFormat ReadBitstreamFormat(AllocationScope scope, ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> data, int operatingPoint)
        => ToBitstreamFormat(ReadSequenceHeader(data, operatingPoint));

    public override HeifDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new AvifDecoder(request, context);

    public override uint CodedItemType => HeifFormat.Av01;

    public override uint SampleEntryType => HeifFormat.Av01;

    public override ReadOnlySpan<byte> AlphaAuxiliaryTypeToWrite => HeifFormat.AlphaAuxiliaryType;

    public override void WriteFileTypeBrands(HeifBoxWriter writer, bool isSequence, HeifStreamDescription color, HeifStreamDescription? alpha)
    {
        writer.WriteUInt32(isSequence ? HeifFormat.Avis : HeifFormat.Avif);
        writer.WriteUInt32(0);
        writer.WriteUInt32(HeifFormat.Avif);
        if (isSequence)
        {
            writer.WriteUInt32(HeifFormat.Avis);
            writer.WriteUInt32(HeifFormat.Msf1);
            writer.WriteUInt32(HeifFormat.Iso8);
        }

        writer.WriteUInt32(HeifFormat.Mif1);
        writer.WriteUInt32(HeifFormat.Miaf);
        if (GetProfileBrand(((AvifStreamDescription)color).Sequence, ((AvifStreamDescription?)alpha)?.Sequence) is { } brand)
            writer.WriteUInt32(brand);
    }

    public override HeifPictureEncoder CreatePictureEncoder(AllocationScope scope, in HeifPictureSettings settings) => new AvifPictureEncoder(scope, settings);

    // AV1 Image File Format specification, section 8 "Profiles": the Baseline profile holds AV1 Main profile images up to
    // level 5.1, the Advanced profile AV1 High profile images up to level 6.0
    private static uint? GetProfileBrand(Av1SequenceHeader color, Av1SequenceHeader? alpha)
    {
        var profile = Math.Max(color.Profile, alpha?.Profile ?? 0);
        var level = Math.Max(color.GetOperatingPoint(0).LevelIndex, alpha?.GetOperatingPoint(0).LevelIndex ?? 0);
        return profile switch
        {
            0 when level <= Av1Levels.Level51 => HeifFormat.MA1B,
            1 when level <= Av1Levels.Level60 => HeifFormat.MA1A,
            _ => null,
        };
    }

    /// <summary>
    /// Reads the sequence header of the data of a coded item or of a sample, for an operating point: the one the first frame
    /// is decoded with, which is the last of the sequence headers before the first frame header, as for the decoder.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The data have no sequence header, or it does not have the operating point.</exception>
    public static Av1SequenceHeader ReadSequenceHeader(ReadOnlySpan<byte> data, int operatingPoint = 0)
    {
        Av1SequenceHeader? sequence = null;
        while (!data.IsEmpty)
        {
            var obu = Av1ObuHeader.Read(data);
            if (obu.Type is Av1Constants.ObuSequenceHeader)
            {
                sequence = Av1SequenceHeader.Parse(data.Slice(obu.HeaderLength, obu.PayloadLength), operatingPoint);
            }
            else if (sequence is not null && obu.Type is Av1Constants.ObuFrameHeader or Av1Constants.ObuFrame)
            {
                break;
            }

            data = data[obu.TotalLength..];
        }

        return sequence ?? throw Instance.Invalid("an AV1 image has no sequence header.");
    }

    /// <summary>Gets the sample format and the color description of a sequence header.</summary>
    public static HeifBitstreamFormat ToBitstreamFormat(Av1SequenceHeader sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        return new HeifBitstreamFormat(
            sequence.BitDepth,
            sequence.Monochrome,
            sequence.SubsamplingX,
            sequence.SubsamplingY,
            sequence.ChromaSamplePosition,
            sequence.MatrixCoefficients,
            sequence.FullRange,
            sequence.ColorDescriptionPresent,
            sequence.ColorPrimaries,
            sequence.TransferCharacteristics);
    }
}
