using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>
/// The library-independent description of a decoded image, compared with a fixture by <see cref="GoldenAssert.ImageMatches"/>.
/// </summary>
/// <remarks>
/// Created from an <c>Image</c> by <see cref="Adapters.ImageSnapshots"/>: each frame's rows are copied through
/// <c>ProcessPixelBytes</c>/<c>ProcessPixelRows</c> into a <see cref="RawPixelBufferBuilder"/>
/// (<see cref="RawPixelBufferBuilder.SetRow16"/> for <c>Rgba64</c>/<c>Gray16</c>), <c>FrameMetadata.Duration</c> into
/// <see cref="RationalDuration.Create"/>, plus <c>PosterFrame</c>, <c>Animation?.TotalPlays</c>, <c>Metadata.Orientation</c>
/// and <c>PixelFormat</c>. Never encode the decoded image to compare it.
/// </remarks>
public sealed class DecodedImageSnapshot
{
    /// <summary>Gets the displayed frames (full canvas).</summary>
    public required IReadOnlyList<DecodedFrameSnapshot> Frames { get; init; }

    /// <summary>Gets the separate poster, or <see langword="null"/>.</summary>
    public RawPixelBuffer? Poster { get; init; }

    /// <summary>Gets a value indicating whether the image has animation settings (<c>Image.Animation != null</c>).</summary>
    public required bool HasAnimation { get; init; }

    /// <summary>Gets the total plays (<see langword="null"/> = infinite); ignored when <see cref="HasAnimation"/> is false.</summary>
    public int? TotalPlays { get; init; }

    /// <summary>Gets the EXIF orientation value exposed by the metadata (1-8).</summary>
    public required int Orientation { get; init; }

    /// <summary>
    /// Gets the decoded metadata (resolution, ICC/EXIF/XMP payloads, text entries), or <see langword="null"/> to skip the
    /// metadata checks. Codec golden tests should provide it: raw pixel files carry no metadata.
    /// </summary>
    public DecodedMetadataSnapshot? Metadata { get; init; }

    /// <summary>Gets the pixel format name of the decoded image (e.g. <c>Rgba32</c>), or <see langword="null"/> to skip the check (typed loads).</summary>
    public string? PixelFormat { get; init; }
}
