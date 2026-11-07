using Meziantou.Framework.Imaging.TestHarness.Gif;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>The result of <see cref="GifOutputVerifier.Verify"/>.</summary>
/// <param name="Reference">The parsed file.</param>
/// <param name="Displayed">The displayed frames decoded by the reference reader (8-bit RGBA).</param>
/// <param name="Intents">The intended frames after the alpha policy (8-bit RGBA).</param>
/// <param name="Errors">Per frame: <see langword="null"/> when the frame was verified exactly, otherwise the quantization error of its opaque pixels.</param>
public sealed record GifVerification(ReferenceGif Reference, IReadOnlyList<RawPixelBuffer> Displayed, IReadOnlyList<RawPixelBuffer> Intents, IReadOnlyList<ReconstructionError?> Errors);
