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
/// In linear light, every paint is combined with the pixels by <see cref="DrawingKernels"/> on the values the
/// transfer function of the image decodes its samples to (<see cref="Image.GetLinearLightTransfer"/>: sRGB unless the
/// image declares another function): the shapes, the images, the layers when they are drawn on what is under them,
/// and the shadows. The layers and the tiles of the patterns hold samples of the image: they have its function.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private const uint HalfCoverage = CoverageRasterizer.FullCoverage / 2;

    private bool _isAliased;
    // The transfer characteristics whose function decodes the samples to the light they are combined in, or 0 when
    // they are combined as they are stored
    private byte _transfer;
    private CompositingSpace _compositingSpace;

    // The transfer characteristics of the image for linear light, once they are known: 0 for samples that already are
    // linear light, which are combined as they are
    private byte? _imageTransfer;

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
    /// <exception cref="UnsupportedImageFeatureException">Linear light is requested for an image whose ICC profile is not recognized as sRGB, or whose transfer characteristics have no function.</exception>
    public CompositingSpace SetCompositingSpace(CompositingSpace space)
    {
        if (space is not (CompositingSpace.Encoded or CompositingSpace.LinearSrgb))
            throw new ArgumentOutOfRangeException(nameof(space), space, "The compositing space is not valid.");

        _cancellationToken.ThrowIfCancellationRequested();
        if (space == CompositingSpace.LinearSrgb)
        {
            _imageTransfer ??= _image.GetLinearLightTransfer("compositing", "composite");
        }

        var previous = _compositingSpace;
        _compositingSpace = space;
        _transfer = space == CompositingSpace.LinearSrgb ? _imageTransfer.GetValueOrDefault() : (byte)0;
        return previous;
    }

    /// <summary>Sets antialiasing back to what <see cref="SetAntialiasing"/> returned: nothing is rented, and nothing fails.</summary>
    public void RestoreAntialiasing(bool enabled) => _isAliased = !enabled;

    /// <summary>Sets the space back to what <see cref="SetCompositingSpace"/> returned: a space that was already validated.</summary>
    public void RestoreCompositingSpace(CompositingSpace space)
    {
        _compositingSpace = space;
        _transfer = space == CompositingSpace.LinearSrgb ? _imageTransfer.GetValueOrDefault() : (byte)0;
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
