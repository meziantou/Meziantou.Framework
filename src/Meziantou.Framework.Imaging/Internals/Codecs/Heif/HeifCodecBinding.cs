namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a format of the HEIF family adds to the container rules that <see cref="HeifStructureParser"/> walks and that
/// <see cref="HeifFileWriter"/> writes: its brands, the type of its coded items and of its sample entries, its configuration
/// record, the header of its bitstream, its pixel decoder and its picture encoder. The container layer is written once; a
/// binding is the only place that knows the codec.
/// </summary>
internal abstract class HeifCodecBinding
{
    /// <summary>Gets the format that the binding reads and writes, which errors report.</summary>
    public abstract ImageFormat Format { get; }

    /// <summary>Gets the name of the format, for messages.</summary>
    public abstract string Name { get; }

    /// <summary>Gets the name of the codec of the coded images, for messages.</summary>
    public abstract string CodecName { get; }

    /// <summary>Gets the type of the property and of the sample entry box that holds the configuration record of the codec.</summary>
    public abstract uint ConfigurationBoxType { get; }

    /// <summary>
    /// Gets the formats that a file whose file type box <paramref name="preferred"/> was detected from may turn out to have:
    /// the preferred one first.
    /// </summary>
    public static ReadOnlySpan<HeifCodecBinding> GetCandidates(HeifCodecBinding preferred)
        => ReferenceEquals(preferred, HeicBinding.Instance) ? HeicFirst : AvifFirst;

    private static readonly HeifCodecBinding[] AvifFirst = [AvifBinding.Instance, HeicBinding.Instance];
    private static readonly HeifCodecBinding[] HeicFirst = [HeicBinding.Instance, AvifBinding.Instance];

    /// <summary>Gets the optional features of the container that the format reads and writes.</summary>
    public virtual HeifContainerFeatures ContainerFeatures => HeifContainerFeatures.None;

    /// <summary>Returns whether the format reads, and writes, an optional feature of the container.</summary>
    public bool Supports(HeifContainerFeatures feature) => (ContainerFeatures & feature) == feature;

    /// <summary>
    /// Gets the largest number of samples that the codec may need after the sample of an image before it outputs the image:
    /// 0 for a codec whose samples are in presentation order. A codec that reorders its images gives the presentation time
    /// of its samples in the composition offsets of its tracks.
    /// </summary>
    public virtual int MaxReorderedSamples => 0;

    /// <summary>
    /// Gets a value indicating whether the chroma samples of planes that have half the columns are, for the codec, at the
    /// position of the luma samples of even columns and not between two of them: where the encoder computes them.
    /// </summary>
    public virtual bool HasCositedChromaColumns => false;

    /// <summary>The file violates the container specifications.</summary>
    public InvalidImageContentException Invalid(string message) => new($"The {Name} file is invalid: " + message, Format);

    /// <summary>The file is valid but uses a feature that the decoder does not implement.</summary>
    public UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, Format, feature);

    /// <summary>Returns whether a brand of the file type box declares a still image of the format.</summary>
    public abstract bool IsImageBrand(uint brand);

    /// <summary>Returns whether a brand of the file type box declares an image sequence of the format.</summary>
    public abstract bool IsSequenceBrand(uint brand);

    /// <summary>Returns whether an item of this type is an image coded with the codec.</summary>
    public abstract bool IsCodedItemType(uint type);

    /// <summary>Returns whether a sample entry of this type describes samples coded with the codec.</summary>
    public abstract bool IsSampleEntryType(uint type);

    /// <summary>Returns whether an auxiliary type (<c>auxC</c>, <c>auxi</c>) is the one of an alpha plane.</summary>
    public abstract bool IsAlphaAuxiliaryType(ReadOnlySpan<byte> auxiliaryType);

    /// <summary>Gets the pixel format of an untyped load of an image.</summary>
    public abstract PixelFormat GetDefaultPixelFormat(bool isMonochrome, bool hasAlpha, int bitDepth);

    /// <summary>Reads the sample format that a configuration record declares.</summary>
    /// <param name="reader">The payload of the configuration box.</param>
    /// <exception cref="InvalidImageContentException">The record is not valid.</exception>
    public abstract HeifSampleFormat ReadConfigurationRecord(ref HeifBoxReader reader);

    /// <summary>
    /// Reads a property of an image item that only the codec defines (the configuration record is read by
    /// <see cref="ReadConfigurationRecord"/>).
    /// </summary>
    /// <param name="type">The type of the property.</param>
    /// <param name="reader">The payload of the property.</param>
    /// <param name="isCodedItem">Whether the item is a coded image, not a derived one.</param>
    /// <param name="image">The image that the property is associated with.</param>
    /// <returns><see langword="true"/> when the codec defines the property; <see langword="false"/> for a property it does not know.</returns>
    public abstract bool TryReadItemProperty(uint type, ref HeifBoxReader reader, bool isCodedItem, HeifCodedImage image);

    /// <summary>
    /// Gets a value indicating whether the decoder needs the configuration record of a coded image or of a track (its
    /// parameter sets), which the walker then keeps: <see cref="HeifStructureParser.GetConfigurationRecord(HeifCodedImage, int)"/>
    /// and <see cref="HeifTrack.ConfigurationRecord"/>.
    /// </summary>
    public virtual bool NeedsConfigurationRecord => false;

    /// <summary>
    /// Looks for the header that <see cref="ReadBitstreamFormat"/> reads: in the configuration record, or else in the first
    /// bytes of the data of a coded item or of a sample.
    /// </summary>
    /// <param name="configuration">The payload of the configuration box of the image or of the track.</param>
    /// <param name="data">The first bytes of the data.</param>
    /// <param name="end">The number of bytes of <paramref name="data"/> that hold the header: 0 when the configuration record has it.</param>
    /// <returns>
    /// <see langword="true"/> when the header is whole; <see langword="false"/> when the data cannot lead to one;
    /// <see langword="null"/> when more bytes are needed to tell.
    /// </returns>
    public abstract bool? TryLocateBitstreamHeader(ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> data, out int end);

    /// <summary>Reads the sample format and the color description of the bitstream of a coded item or of a sample.</summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="configuration">The payload of the configuration box of the image or of the track.</param>
    /// <param name="data">The data, or the part of them that <see cref="TryLocateBitstreamHeader"/> delimits.</param>
    /// <param name="operatingPoint">The operating point that the image selects.</param>
    /// <exception cref="InvalidImageContentException">The bitstream does not have the header, or it is not valid.</exception>
    public abstract HeifBitstreamFormat ReadBitstreamFormat(AllocationScope scope, ReadOnlySpan<byte> configuration, ReadOnlySpan<byte> data, int operatingPoint);

    /// <summary>Creates the pixel decoder of a file of the format.</summary>
    public abstract HeifDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context);

    /// <summary>Gets the type that <see cref="HeifFileWriter"/> gives to an item that is an image coded with the codec.</summary>
    public abstract uint CodedItemType { get; }

    /// <summary>Gets the type that <see cref="HeifFileWriter"/> gives to the sample entry of a track of images coded with the codec.</summary>
    public abstract uint SampleEntryType { get; }

    /// <summary>Gets the auxiliary type (<c>auxC</c>, <c>auxi</c>) that <see cref="HeifFileWriter"/> gives to an alpha plane.</summary>
    public abstract ReadOnlySpan<byte> AlphaAuxiliaryTypeToWrite { get; }

    /// <summary>
    /// Writes the content of the file type box (ISO/IEC 14496-12, section 4.3) of a file that <see cref="HeifFileWriter"/>
    /// writes: the major brand, the minor version and the compatible brands, which depend on the profile of the streams.
    /// </summary>
    /// <param name="writer">The writer, in the file type box.</param>
    /// <param name="isSequence">Whether the file is an image sequence, which also describes its first image as a still image.</param>
    /// <param name="color">The description of the color images.</param>
    /// <param name="alpha">The description of the alpha planes, or <see langword="null"/> for an opaque file.</param>
    public abstract void WriteFileTypeBrands(HeifBoxWriter writer, bool isSequence, HeifStreamDescription color, HeifStreamDescription? alpha);

    /// <summary>Creates the encoder of a coded stream of a file of the format, which rents its buffers from <paramref name="scope"/>.</summary>
    /// <exception cref="ImageResourceLimitException">A buffer exceeds the live-allocation limit.</exception>
    public abstract HeifPictureEncoder CreatePictureEncoder(AllocationScope scope, in HeifPictureSettings settings);
}
