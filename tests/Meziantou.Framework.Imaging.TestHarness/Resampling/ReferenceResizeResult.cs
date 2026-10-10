using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The exact output samples of a reference resize, and their comparison with actual pixels.</summary>
public sealed class ReferenceResizeResult : ReferenceFilterResult
{
    internal ReferenceResizeResult(ReferenceResizeOptions options, int width, int height, RawPixelLayout layout, decimal[] values, bool isUnchanged = false)
        : base(options.ToString(), width, height, layout, values, zeroAlphaIsTransparentBlack: !isUnchanged, artifactDirectory: "resize")
    {
        IsUnchanged = isUnchanged;
        Options = options;
    }

    /// <summary>Gets the options.</summary>
    public ReferenceResizeOptions Options { get; }

    /// <summary>Gets a value indicating whether the resize keeps the canvas size, which leaves the pixels unchanged (hidden colors of transparent pixels included).</summary>
    public bool IsUnchanged { get; }
}
