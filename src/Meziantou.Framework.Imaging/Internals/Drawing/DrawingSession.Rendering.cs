using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>How the draw calls of a callback are rendered: with or without antialiasing, and in which space their paints are combined with the pixels.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> Without antialiasing, the coverage of a pixel is all or nothing: a pixel whose exact coverage,
/// rounded as usual, is at least half (<c>32768 / 65536</c>) is covered entirely, and the others not at all. This
/// applies to the shapes, the strokes, the texts and the images that are drawn, and to the clips that start, while
/// antialiasing is off; a clip keeps the coverages it started with. The opacities (of a paint, of an image, of a
/// layer) and the blur of a shadow are not edges: they are unchanged, and so are the glyphs a font draws with its
/// own colors.
/// </para>
/// <para>
/// In linear light, every paint is combined with the pixels by <see cref="DrawingKernels"/> on decoded sRGB values:
/// the shapes, the images, the layers when they are drawn on what is under them, and the shadows.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private const uint HalfCoverage = CoverageRasterizer.FullCoverage / 2;

    private bool _isAliased;
    private bool _isLinear;
    private CompositingSpace _compositingSpace;

    // Whether linear light needs the samples to be decoded (they are sRGB values), once it is known: samples that
    // already are linear light are combined as they are
    private bool? _decodesSamples;

    /// <summary>Turns antialiasing on or off for the draw calls that follow.</summary>
    /// <returns>Whether antialiasing was on.</returns>
    /// <exception cref="ImageResourceLimitException">The row the coverages are written to exceeds the allocation limit.</exception>
    public bool SetAntialiasing(bool enabled)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!enabled)
        {
            // The coverages of a run are rewritten: the row they go to is rented before a draw call needs it
            EnsureCoverageRow();
        }

        var previous = !_isAliased;
        _isAliased = !enabled;
        return previous;
    }

    /// <summary>Sets the space in which the paints of the draw calls that follow are combined with the pixels.</summary>
    /// <returns>The space that was set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The space is not defined.</exception>
    /// <exception cref="UnsupportedImageFeatureException">Linear light is requested for an image whose color profile is not recognized as sRGB.</exception>
    public CompositingSpace SetCompositingSpace(CompositingSpace space)
    {
        if (space is not (CompositingSpace.Encoded or CompositingSpace.LinearSrgb))
            throw new ArgumentOutOfRangeException(nameof(space), space, "The compositing space is not valid.");

        _cancellationToken.ThrowIfCancellationRequested();
        if (space == CompositingSpace.LinearSrgb)
        {
            _decodesSamples ??= _image.UseLinearLight("compositing", "composite");
        }

        var previous = _compositingSpace;
        _compositingSpace = space;
        _isLinear = space == CompositingSpace.LinearSrgb && _decodesSamples == true;
        return previous;
    }

    /// <summary>Sets antialiasing back to what <see cref="SetAntialiasing"/> returned: nothing is rented, and nothing fails.</summary>
    public void RestoreAntialiasing(bool enabled) => _isAliased = !enabled;

    /// <summary>Sets the space back to what <see cref="SetCompositingSpace"/> returned: a space that was already validated.</summary>
    public void RestoreCompositingSpace(CompositingSpace space)
    {
        _compositingSpace = space;
        _isLinear = space == CompositingSpace.LinearSrgb && _decodesSamples == true;
    }

    /// <summary>Makes the coverages of a run all or nothing, into the row of the session.</summary>
    /// <returns><see langword="false"/> when no pixel of the run is covered.</returns>
    private bool RemoveAntialiasing(ref ReadOnlySpan<uint> coverage)
    {
        var row = unsafe(MemoryMarshal.Cast<byte, uint>(_clipRow!.Span))[..coverage.Length];
        var any = RemoveAntialiasing(coverage, row);
        coverage = row;
        return any;
    }

    private static bool RemoveAntialiasing(ReadOnlySpan<uint> coverage, Span<uint> result)
    {
        var any = false;
        for (var i = 0; i < coverage.Length; i++)
        {
            var isCovered = coverage[i] >= HalfCoverage;
            result[i] = isCovered ? CoverageRasterizer.FullCoverage : 0;
            any |= isCovered;
        }

        return any;
    }
}
