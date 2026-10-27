using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The exact output samples of a reference transformation, and their comparison with actual pixels.</summary>
public sealed class ReferenceTransformResult : ReferenceFilterResult
{
    internal ReferenceTransformResult(ReferenceTransformOptions options, int width, int height, RawPixelLayout layout, decimal[] values, bool zeroAlphaIsTransparentBlack)
        : base(options.ToString(), width, height, layout, values, zeroAlphaIsTransparentBlack, artifactDirectory: "transform")
    {
        Options = options;
    }

    /// <summary>Gets the options.</summary>
    public ReferenceTransformOptions Options { get; }
}
