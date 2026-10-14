using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Metadata;

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
/// image declares another function), or the tone curves of its ICC profile when the profile is not the one of sRGB
/// (<see cref="LinearLightCurves"/>): the shapes, the images, the layers when they are drawn on what is under them,
/// the shadows, and the blurs, which then add light. The layers and the tiles of the patterns hold samples of the
/// image: they have its function.
/// </para>
/// <para>
/// <b>Paint colors.</b> Inside <see cref="DrawingCanvas.PaintProfile(IccProfile, DrawingAction, IccColorTransformOptions)"/>,
/// the colors that the caller gives are colors of a profile: the color of a solid brush, the stops of a gradient, the
/// color of a shadow and the color the canvas is cleared with go through <see cref="PaintColorConverter"/> before
/// they are converted to the pixel format. A gradient that mixes its stops in linear light or in Oklab mixes the
/// colors as they are given, and converts each color it obtains. What already is samples of an image is not
/// converted: the images that are drawn, the tiles of the patterns (whose drawings were), the layers, and the colors
/// a font gives to its glyphs.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private const uint HalfCoverage = CoverageRasterizer.FullCoverage / 2;

    private bool _isAliased;
    // The transfer characteristics whose function decodes the samples to the light they are combined in, or 0 when
    // they are combined as they are stored, or when the tone curves of an ICC profile decode them
    private byte _transfer;
    private CompositingSpace _compositingSpace;

    // The transfer characteristics of the image for linear light, once they are known: 0 for samples that already are
    // linear light, which are combined as they are, and for those that the curves of an ICC profile decode
    private byte? _imageTransfer;

    // The tone curves of the ICC profile of the image, when they are what decodes its samples to linear light, and
    // those the draw calls use: the same ones in linear light, none otherwise
    private LinearLightCurves? _imageCurves;
    private LinearLightCurves? _curves;
    private bool _ownsImageCurves;

    // The image whose color space the samples that are drawn have: the image that is drawn on, also for the session
    // that draws the tile of one of its patterns
    private Image? _colorImage;

    // The conversion that brings the colors of the paints to the color space of the image, or none when the paints
    // are sample values of the image
    private IccPipeline? _paintPipeline;

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
    /// <exception cref="UnsupportedImageFeatureException">Linear light is requested for an image whose ICC profile has no tone curve per channel, or whose transfer characteristics have no function.</exception>
    /// <exception cref="ImageResourceLimitException">The tables of the tone curves of the profile exceed the allocation limit.</exception>
    public CompositingSpace SetCompositingSpace(CompositingSpace space)
    {
        if (space is not (CompositingSpace.Encoded or CompositingSpace.LinearSrgb))
            throw new ArgumentOutOfRangeException(nameof(space), space, "The compositing space is not valid.");

        _cancellationToken.ThrowIfCancellationRequested();
        if (space == CompositingSpace.LinearSrgb && _imageTransfer is null)
        {
            // The tone curves of a profile that is not the one of sRGB, or else the function the image declares
            var image = _colorImage ?? _image;
            _imageCurves = image.CreateLinearLightCurves();
            _ownsImageCurves = _imageCurves is not null;
            _imageTransfer = _imageCurves is null ? image.GetLinearLightTransfer("compositing", "composite") : (byte)0;
        }

        var previous = _compositingSpace;
        RestoreCompositingSpace(space);
        return previous;
    }

    /// <summary>Sets antialiasing back to what <see cref="SetAntialiasing"/> returned: nothing is rented, and nothing fails.</summary>
    public void RestoreAntialiasing(bool enabled) => _isAliased = !enabled;

    /// <summary>Sets the space back to what <see cref="SetCompositingSpace"/> returned: a space that was already validated.</summary>
    public void RestoreCompositingSpace(CompositingSpace space)
    {
        _compositingSpace = space;
        var isLinear = space == CompositingSpace.LinearSrgb;
        _transfer = isLinear ? _imageTransfer.GetValueOrDefault() : (byte)0;
        _curves = isLinear ? _imageCurves : null;
    }

    /// <summary>Says that the colors of the paints of the draw calls that follow are colors of a profile: they are converted to the color space of the image.</summary>
    /// <returns>The conversion that was set, to give back to <see cref="RestorePaintProfile"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A profile is not supported in its role, or the color space of the image is unknown, ambiguous or of a high dynamic range.</exception>
    public IccPipeline? SetPaintProfile(IccProfile profile, IccColorTransformOptions? options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _cancellationToken.ThrowIfCancellationRequested();
        var pipeline = (_colorImage ?? _image).CreatePaintPipeline(profile, options ?? IccColorTransformOptions.Default);
        var previous = _paintPipeline;
        _paintPipeline = pipeline;
        return previous;
    }

    /// <summary>Sets the conversion of the paints back to what <see cref="SetPaintProfile"/> returned.</summary>
    public void RestorePaintProfile(IccPipeline? previous) => _paintPipeline = previous;

    /// <summary>Gets the color a paint has in the color space of the image.</summary>
    private Rgba64 ToImageColor(Rgba64 color) => PaintColorConverter.Convert(_paintPipeline, color);

    private void DisposeRendering()
    {
        if (_ownsImageCurves)
        {
            _imageCurves?.Dispose();
        }

        _imageCurves = null;
        _curves = null;
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
