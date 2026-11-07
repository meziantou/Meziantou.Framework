using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The exact properties of the pixels of every image known before the header of a <see cref="Formats.PngOptimization.MinimumSize"/>
/// output, and the candidate layouts (<see cref="PngOptimizedLayout"/>) that store all of them.
/// </summary>
/// <remarks>
/// <para>
/// The analysis reads wide rows. It records whether every pixel is opaque, whether every pixel is gray, whether every
/// 16-bit sample is an 8-bit value (both bytes equal), the bit depths that hold every gray sample, whether the
/// transparent pixels can be a <c>tRNS</c> color key (they are all fully transparent with one color that no opaque pixel
/// has), and the distinct colors when there are at most 256 of them and they are 8-bit colors. Adding an image twice
/// changes nothing, so an image may be analyzed more than once.
/// </para>
/// <para>
/// When the colors of the opaque pixels are not recorded, an image is read until nothing more can change: the colors do not
/// fit a palette, a pixel is not gray (or the gray samples need every bit), a pixel is not opaque (or the pixel format has
/// no alpha), and a 16-bit sample is not an 8-bit value (or the samples are 8-bit). The other rows are skipped.
/// </para>
/// <para>
/// The colors of the opaque pixels are recorded in a set of 2^24 bits (2 MiB, charged to the writer scope) keyed by the
/// high bytes of their samples, when the pixel format has alpha (for the color key) or the output is an animation (for the
/// unused color key, <see cref="CreateCandidates"/>). For 16-bit samples the set is conservative: two colors with the same
/// high bytes share a bit, which can only make a color key unavailable.
/// </para>
/// </remarks>
internal sealed class PngLayoutAnalysis : IDisposable
{
    private const int DepthMask1 = 1 << 0;
    private const int DepthMask2 = 1 << 1;
    private const int DepthMask4 = 1 << 2;
    private const int DepthMask8 = 1 << 3;
    private const int DepthMask16 = 1 << 4;

    private readonly AllocationScope _scope;
    private readonly bool _isWide16;
    private readonly bool _hasAlpha;
    private readonly List<PngIndexedRows> _copies = [];
    private PngIndexedRows? _palette;
    private PooledBuffer? _opaqueColors;
    private State _state;

    /// <summary>Creates the analysis of images of one width, and rents its working memory.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="isWide16">Whether the wide rows are 16-bit.</param>
    /// <param name="width">The image width.</param>
    /// <param name="trackOpaqueColors">Whether the colors of the opaque pixels are recorded.</param>
    /// <param name="hasAlpha">Whether the pixel format of the images has alpha (otherwise every pixel is opaque).</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public PngLayoutAnalysis(AllocationScope scope, bool isWide16, int width, bool trackOpaqueColors, bool hasAlpha)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _isWide16 = isWide16;
        _hasAlpha = hasAlpha;
        _state = new State(Opaque: true, Gray: true, EightBit: true, GrayDepths: DepthMask1 | DepthMask2 | DepthMask4 | DepthMask8 | (isWide16 ? DepthMask16 : 0), KeyPossible: true, HasTransparent: false, TransparentColor: default, PaletteFits: true);
        try
        {
            _palette = new PngIndexedRows(scope, isWide16 ? PixelFormat.Rgba64 : PixelFormat.Rgba32, width);
            if (trackOpaqueColors)
            {
                _opaqueColors = scope.Rent((1 << 24) / 8, AllocationKind.Temporary);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the palette of the colors of the images (8-bit colors only, at most 256), or <see langword="null"/> when they do not fit.</summary>
    public PngIndexedRows? Palette => _state.PaletteFits ? _palette : null;

    /// <summary>
    /// Adds the pixels of an image. When <paramref name="paletteRequired"/> is set and the colors do not fit a palette, the
    /// analysis is left as it was before the image (the colors of opaque pixels excepted, which only makes a color key
    /// less available) and the failure is reported.
    /// </summary>
    /// <param name="rows">The wide rows of the image.</param>
    /// <param name="paletteRequired">Whether the image must fit the palette.</param>
    /// <param name="x">The column of the pixel that does not fit the palette.</param>
    /// <param name="y">The row of the pixel that does not fit the palette.</param>
    /// <returns><see cref="PngIndexedRowStatus.Ok"/>, or why the image does not fit a required palette.</returns>
    public PngIndexedRowStatus Add(PngWideRows rows, bool paletteRequired, out int x, out int y)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var palette = _palette ?? throw new ObjectDisposedException(nameof(PngLayoutAnalysis));
        var saved = _state;
        var count = palette.Count;
        for (y = 0; y < rows.Height; y++)
        {
            var row = rows.GetRow(y);
            AddRow(row);
            if (_state.PaletteFits)
            {
                var status = palette.Collect(row, out x);
                if (status != PngIndexedRowStatus.Ok)
                {
                    if (paletteRequired)
                    {
                        _state = saved;
                        palette.Rollback(count);
                        return status;
                    }

                    _state = _state with { PaletteFits = false };
                }
            }

            if (IsSettled)
                break;
        }

        (x, y) = (-1, -1);
        return PngIndexedRowStatus.Ok;
    }

    /// <summary>
    /// Creates the candidate layouts of the images added so far, in order of preference, and freezes the palette. Each one
    /// stores every pixel that was added exactly.
    /// </summary>
    /// <param name="paletteOnly">Whether only palettes are candidates (<see cref="Formats.PngPixelLayout.Indexed"/>).</param>
    /// <param name="addTransparency">
    /// Whether a layout without a transparent value is also a candidate with one (an animation, whose frames can then skip
    /// their unchanged pixels): a color that no pixel has as the color key of an opaque layout, or a transparent palette entry.
    /// Only when <paramref name="allImagesKnown"/> is set, since a later image could have that color.
    /// </param>
    /// <param name="allImagesKnown">Whether every image of the output was added.</param>
    /// <param name="allowGray">Whether grayscale layouts are allowed (an ICC profile of the file describes their color space).</param>
    /// <param name="allowColor">Whether RGB and palette layouts are allowed.</param>
    /// <param name="allowPackedSamples">Whether the samples and the indexes can have less than 8 bits.</param>
    /// <returns>The candidates; never empty.</returns>
    public List<PngOptimizedLayout> CreateCandidates(bool paletteOnly, bool addTransparency, bool allImagesKnown, bool allowGray, bool allowColor, bool allowPackedSamples)
    {
        var state = _state;
        if (!allowGray)
        {
            state = state with { Gray = false };
        }

        var candidates = new List<PngOptimizedLayout>();
        if (!paletteOnly)
        {
            var key = state.KeyPossible && state.HasTransparent && !IsOpaqueColor(state.TransparentColor) ? state.TransparentColor : (PngWideColor?)null;
            var hasAlphaChannel = !state.Opaque && key is null;
            var depth = (byte)(state.EightBit ? 8 : 16);

            // A color key saves the alpha samples but costs a chunk: the alpha channel is a candidate too
            if (key is not null && (state.Gray ? allowGray : allowColor))
            {
                candidates.Add(PngOptimizedLayout.Truecolor(state.Gray ? (byte)4 : (byte)6, depth, _isWide16));
            }

            if (state.Gray)
            {
                if (hasAlphaChannel)
                {
                    candidates.Add(PngOptimizedLayout.Truecolor(4, depth, _isWide16));
                }
                else
                {
                    var grayDepth = (byte)(1 << BitOperations.TrailingZeroCount(allowPackedSamples ? state.GrayDepths : state.GrayDepths & ~(DepthMask1 | DepthMask2 | DepthMask4)));
                    AddTruecolor(colorType: 0, grayDepth, key);
                    if (grayDepth < 8)
                    {
                        AddTruecolor(colorType: 0, bitDepth: 8, key);
                    }
                }
            }
            else if (allowColor)
            {
                if (hasAlphaChannel)
                {
                    candidates.Add(PngOptimizedLayout.Truecolor(6, depth, _isWide16));
                }
                else
                {
                    AddTruecolor(colorType: 2, depth, key);
                }
            }

            void AddTruecolor(byte colorType, byte bitDepth, PngWideColor? key)
            {
                candidates.Add(PngOptimizedLayout.Truecolor(colorType, bitDepth, _isWide16, key));
                if (key is null && state.Opaque && addTransparency && allImagesKnown && TryFindUnusedColor(colorType == 0, bitDepth, out var unused))
                {
                    candidates.Add(PngOptimizedLayout.Truecolor(colorType, bitDepth, _isWide16, unused));
                }
            }
        }

        if (allowColor && Palette is { } palette)
        {
            // The same colors with transparent black, the first entry of the sorted palette whatever the other colors
            PngIndexedRows? transparent = null;
            if (addTransparency && allImagesKnown && palette.Count < ExactPaletteCollector.MaxColors && !HasTransparentEntry(palette.Colors))
            {
                // Owned by the analysis until a candidate takes it
                _copies.Add(palette.CreateCopy(_scope, key: 0));
                transparent = _copies[^1];
            }

            AddIndexed(palette);
            if (transparent is not null)
            {
                AddIndexed(transparent);
            }

            void AddIndexed(PngIndexedRows palette)
            {
                palette.Freeze();
                var bitDepth = allowPackedSamples ? PngIndexedRows.GetBitDepth(palette.Count) : (byte)8;
                candidates.Add(PngOptimizedLayout.Indexed(palette, bitDepth, _isWide16));
                if (bitDepth < 8)
                {
                    candidates.Add(PngOptimizedLayout.Indexed(palette, bitDepth: 8, _isWide16));
                }
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("No PNG layout stores the images with the color space of their profile.");

        return candidates;
    }

    /// <summary>Hands the palette of a candidate over to the caller, which then disposes it; the analysis no longer holds it.</summary>
    /// <param name="palette">The palette of the chosen candidate, or <see langword="null"/>.</param>
    /// <returns><paramref name="palette"/>.</returns>
    public PngIndexedRows? TakePalette(PngIndexedRows? palette)
    {
        if (palette is not null && !_copies.Remove(palette))
        {
            if (!ReferenceEquals(palette, _palette))
                throw new ArgumentException("The palette is not one of the candidates.", nameof(palette));

            _palette = null;
        }

        return palette;
    }

    /// <summary>Gets a value indicating whether no pixel can change the analysis any more.</summary>
    private bool IsSettled
    {
        get
        {
            var state = _state;
            return _opaqueColors is null
                && !state.PaletteFits
                && (!state.Gray || state.GrayDepths == (_isWide16 ? DepthMask16 : DepthMask8))
                && (!state.Opaque || !_hasAlpha)
                && (!state.EightBit || !_isWide16);
        }
    }

    public void Dispose()
    {
        foreach (var copy in _copies)
        {
            copy.Dispose();
        }

        _copies.Clear();
        _opaqueColors?.Dispose();
        _opaqueColors = null;
        _palette?.Dispose();
        _palette = null;
    }

    private static bool HasTransparentEntry(ReadOnlySpan<uint> colors)
    {
        foreach (var color in colors)
        {
            if ((color >> 24) == 0)
                return true;
        }

        return false;
    }

    private static int GetGrayDepths8(uint value)
    {
        var mask = DepthMask8;
        if (value % 17 == 0)
        {
            mask |= DepthMask4;
        }

        if (value % 85 == 0)
        {
            mask |= DepthMask2;
        }

        if (value % 255 == 0)
        {
            mask |= DepthMask1;
        }

        return mask;
    }

    private static int GetGrayDepths16(uint value)
    {
        var mask = DepthMask16;
        if (value % 257 == 0)
        {
            mask |= DepthMask8;
        }

        if (value % 4369 == 0)
        {
            mask |= DepthMask4;
        }

        if (value % 21845 == 0)
        {
            mask |= DepthMask2;
        }

        if (value % 65535 == 0)
        {
            mask |= DepthMask1;
        }

        return mask;
    }

    private void AddRow(ReadOnlySpan<byte> row)
    {
        var state = _state;
        var max = _isWide16 ? 65535u : 255u;
        var samples16 = _isWide16 ? unsafe(MemoryMarshal.Cast<byte, ushort>(row)) : default;
        var trackColors = _opaqueColors is not null;
        var colors = trackColors ? _opaqueColors!.Span : default;
        var width = row.Length / (_isWide16 ? 8 : 4);
        var lastOpaqueIndex = -1;
        for (var x = 0; x < width; x++)
        {
            uint r, g, b, a;
            if (_isWide16)
            {
                var pixel = samples16.Slice(x * 4, 4);
                (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
                if (state.EightBit && (!IsEightBit(r) || !IsEightBit(g) || !IsEightBit(b) || !IsEightBit(a)))
                {
                    state = state with { EightBit = false };
                }
            }
            else
            {
                var pixel = row.Slice(x * 4, 4);
                (r, g, b, a) = (pixel[0], pixel[1], pixel[2], pixel[3]);
            }

            if (state.Gray)
            {
                if (r != g || r != b)
                {
                    state = state with { Gray = false };
                }
                else if (state.GrayDepths != (_isWide16 ? DepthMask16 : DepthMask8))
                {
                    state = state with { GrayDepths = state.GrayDepths & (_isWide16 ? GetGrayDepths16(r) : GetGrayDepths8(r)) };
                }
            }

            if (a == max)
            {
                if (trackColors)
                {
                    var index = _isWide16 ? (int)(((r >> 8) << 16) | ((g >> 8) << 8) | (b >> 8)) : (int)((r << 16) | (g << 8) | b);
                    if (index != lastOpaqueIndex)
                    {
                        colors[index >> 3] |= (byte)(1 << (index & 7));
                        lastOpaqueIndex = index;
                    }
                }

                continue;
            }

            if (state.Opaque)
            {
                state = state with { Opaque = false };
            }

            if (!state.KeyPossible)
                continue;

            var color = new PngWideColor((ushort)r, (ushort)g, (ushort)b);
            if (a != 0 || (state.HasTransparent && color != state.TransparentColor))
            {
                state = state with { KeyPossible = false };
            }
            else if (!state.HasTransparent)
            {
                state = state with { HasTransparent = true, TransparentColor = color };
            }
        }

        _state = state;

        static bool IsEightBit(uint sample) => (sample >> 8) == (sample & 0xFF);
    }

    /// <summary>Gets a value indicating whether an opaque pixel may have a color (always, when colors are not recorded).</summary>
    private bool IsOpaqueColor(PngWideColor color)
    {
        if (_opaqueColors is not { } set)
            return true;

        var index = _isWide16 ? ((color.R >> 8) << 16) | ((color.G >> 8) << 8) | (color.B >> 8) : (color.R << 16) | (color.G << 8) | color.B;
        return (set.Span[index >> 3] & (1 << (index & 7))) != 0;
    }

    /// <summary>Finds a color that no pixel has and that a layout of the bit depth can store, for the color key of an opaque layout.</summary>
    private bool TryFindUnusedColor(bool gray, byte bitDepth, out PngWideColor color)
    {
        color = default;
        if (_opaqueColors is not { } set)
            return false;

        var span = set.Span;
        if (gray)
        {
            // The levels of the bit depth, as 8-bit values (the high bytes of their 16-bit values)
            var levels = bitDepth >= 8 ? 256 : 1 << bitDepth;
            var step = 255 / (levels - 1);
            for (var level = 0; level < levels; level++)
            {
                var value = level * step;
                var index = (value << 16) | (value << 8) | value;
                if ((span[index >> 3] & (1 << (index & 7))) == 0)
                {
                    var wide = (ushort)(_isWide16 ? value * 257 : value);
                    color = new PngWideColor(wide, wide, wide);
                    return true;
                }
            }

            return false;
        }

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == byte.MaxValue)
                continue;

            var index = (i << 3) + BitOperations.TrailingZeroCount(~span[i] & 0xFF);
            var (r, g, b) = ((index >> 16) & 0xFF, (index >> 8) & 0xFF, index & 0xFF);
            color = _isWide16 ? new PngWideColor((ushort)(r * 257), (ushort)(g * 257), (ushort)(b * 257)) : new PngWideColor((ushort)r, (ushort)g, (ushort)b);
            return true;
        }

        return false;
    }

    private readonly record struct State(bool Opaque, bool Gray, bool EightBit, int GrayDepths, bool KeyPossible, bool HasTransparent, PngWideColor TransparentColor, bool PaletteFits);
}
