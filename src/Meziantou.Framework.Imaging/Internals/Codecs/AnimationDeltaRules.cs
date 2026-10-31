using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What a format can write when <see cref="AnimationFrameDelta"/> stores its frames as regions of the canvas.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnimationDeltaRules
{
    /// <summary>Gets the bytes per pixel: 4 (<see cref="PixelFormat.Rgba32"/>) or 8 (<see cref="PixelFormat.Rgba64"/>).</summary>
    public int BytesPerPixel { get; init; }

    /// <summary>Gets a value indicating whether a region can replace the canvas, alpha included (APNG SOURCE, WebP "do not blend"); GIF cannot.</summary>
    public bool SourceBlend { get; init; }

    /// <summary>Gets a value indicating whether the unchanged pixels of a region can be written fully transparent, which leaves the canvas (APNG OVER, WebP blending, the GIF transparent index).</summary>
    public bool OverBlend { get; init; }

    /// <summary>Gets a value indicating whether OVER gives a translucent pixel exactly on a fully transparent canvas pixel (APNG).</summary>
    public bool TranslucentOverTransparent { get; init; }

    /// <summary>Gets a value indicating whether a frame can be disposed by restoring the canvas it was drawn on (APNG, GIF); the canvas is then kept.</summary>
    public bool RestorePrevious { get; init; }

    /// <summary>
    /// Gets a value indicating whether the previous frame is written only once the next one is planned, so that its region
    /// can grow (GIF, which cannot make a pixel transparent); the canvas it is drawn on is then kept.
    /// </summary>
    public bool EnlargePreviousRegion { get; init; }

    /// <summary>Gets a value indicating whether a region starts at even coordinates (WebP).</summary>
    public bool EvenOffsets { get; init; }
}
