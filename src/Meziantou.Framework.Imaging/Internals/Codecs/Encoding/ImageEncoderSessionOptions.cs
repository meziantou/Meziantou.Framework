using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The validated, immutable inputs of an <see cref="ImageEncoderSession"/>.</summary>
/// <param name="Encoder">The encoder settings.</param>
/// <param name="Capabilities">The resolved container constraints (static or animated output, poster support).</param>
/// <param name="CanvasSize">The canvas size of every frame.</param>
/// <param name="PixelFormat">The pixel format of every frame.</param>
/// <param name="ExpectedFrameCount">The number of displayed frames that will be written, or <see langword="null"/> if unknown (GIF only).</param>
/// <param name="Metadata">The metadata to write, computed once with the encoder's metadata policy.</param>
/// <param name="Animation">The animation settings of an animated output (defaults when none were given), or <see langword="null"/> for a static output.</param>
/// <param name="Configuration">The writer configuration.</param>
/// <param name="Scope">The writer allocation scope, charged for every private buffer of the session.</param>
internal sealed record ImageEncoderSessionOptions(
    ImageEncoder Encoder,
    ImageOutputCapabilities Capabilities,
    Size CanvasSize,
    PixelFormat PixelFormat,
    int? ExpectedFrameCount,
    MetadataWritePlan Metadata,
    AnimationMetadata? Animation,
    ImageConfiguration Configuration,
    AllocationScope Scope);
