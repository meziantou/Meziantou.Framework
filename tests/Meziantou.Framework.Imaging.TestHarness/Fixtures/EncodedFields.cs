namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Raw timing, loop and metadata fields read from an encoded input by <see cref="EncodedFieldInspector"/>.</summary>
public sealed class EncodedFields
{
    /// <summary>Gets the raw loop field: APNG <c>acTL num_plays</c>, GIF NETSCAPE2.0 loop count or WebP <c>ANIM</c> loop count; <see langword="null"/> when absent.</summary>
    public int? LoopValue { get; internal set; }

    /// <summary>Gets the raw delay field of each displayed frame (APNG <c>fcTL</c> order, GIF image order, WebP <c>ANMF</c> order); <see langword="null"/> entries have no delay field.</summary>
    public IList<EncodedDelayExpectation?> FrameDelays { get; } = [];

    /// <summary>Gets the encoded physical resolution, or <see langword="null"/> when absent or aspect-ratio only.</summary>
    public EncodedResolution? Resolution { get; internal set; }

    /// <summary>Gets the uncompressed ICC profile, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Icc { get; internal set; }

    /// <summary>Gets the EXIF data from the TIFF header, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Exif { get; internal set; }

    /// <summary>Gets the XMP packet, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Xmp { get; internal set; }

    /// <summary>Gets the transfer function declared by the header (QOI colorspace: <c>srgb</c> for 0, <c>linear</c> for 1), or <see langword="null"/> when the format has no such field.</summary>
    public string? TransferFunction { get; internal set; }

    /// <summary>Gets the text entries in file order.</summary>
    public IList<TextEntryExpectation> Text { get; } = [];
}
