using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color reduction of indexed PNG output (<see cref="PngEncoder.AllowColorReduction"/>): each image is reduced to an 8-bit
/// RGBA copy whose colors are entries of the palette of the file, so that the encoder sessions store it exactly, as an image
/// that was quantized first (<see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The palette is determined once, from every image of an eager save (<see cref="Plan"/>) or from the first image given to a
/// sequential writer: <see cref="QuantizeOptions.Palette"/> when it is set; otherwise the exact colors of the images when
/// there are at most 256 of them and every sample has an 8-bit value (the copies then hold the same colors, and the file is
/// the one that is written without color reduction); otherwise the median cut of their colors
/// (<see cref="QuantizeOptions.MaxColors"/>, the transparent entry included). The palette of a sequential writer is then
/// narrowed to the colors of the copy of its first image: the file palette, which is written before that image.
/// </para>
/// <para>
/// An image whose colors are all palette entries (16-bit samples included, which must then have 8-bit values) is copied as it
/// is; any other image is mapped to the palette, with the dithering of the options, and its transparent pixels get the
/// transparent color of the palette.
/// </para>
/// </remarks>
internal sealed class PngColorReduction
{
    private readonly QuantizeOptions _options;
    private readonly ImageConfiguration _configuration;
    private readonly AllocationScope _scope;
    private readonly Size _canvasSize;
    private readonly bool _sequential;
    private HashSet<uint>? _colors;
    private Rgba32? _transparent;
    private bool _committed;

    /// <summary>Creates the reduction of one output.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="configuration">The configuration of the copies.</param>
    /// <param name="scope">The writer scope, charged for the copies and the working memory.</param>
    /// <param name="canvasSize">The canvas size.</param>
    /// <param name="sequential"><see langword="true"/> for a sequential writer, <see langword="false"/> for an eager save (<see cref="Plan"/>).</param>
    public PngColorReduction(PngEncoder encoder, ImageConfiguration configuration, AllocationScope scope, Size canvasSize, bool sequential)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        _options = encoder.Quantization;
        _configuration = configuration;
        _scope = scope;
        _canvasSize = canvasSize;
        _sequential = sequential;
    }

    /// <summary>Gets a value indicating whether an encoder reduces the colors of indexed PNG output.</summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns><see langword="true"/> for <see cref="PngPixelLayout.Indexed"/> with <see cref="PngEncoder.AllowColorReduction"/>.</returns>
    public static bool Applies(ImageEncoder encoder) => encoder is PngEncoder { PixelLayout: PngPixelLayout.Indexed, AllowColorReduction: true };

    /// <summary>Determines the palette from every image of an eager save, before any copy.</summary>
    /// <param name="images">The poster and the displayed frames.</param>
    public void Plan(IReadOnlyList<ImageFrame> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        DeterminePalette(images);
        _committed = true;
    }

    /// <summary>Makes the palette final: the image it was determined from was accepted by the writer.</summary>
    public void Commit() => _committed = true;

    /// <summary>Forgets a palette that is not final: the image it was determined from was rejected, and the next image determines it.</summary>
    public void Rollback()
    {
        if (!_committed)
        {
            _colors = null;
            _transparent = null;
        }
    }

    /// <summary>Reduces one image to an 8-bit RGBA copy whose colors are palette entries.</summary>
    /// <param name="frame">The image.</param>
    /// <returns>The copy, which the caller disposes.</returns>
    public Image<Rgba32> Reduce(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var first = _colors is null;
        if (first)
        {
            DeterminePalette([frame]);
        }

        var copy = new Image<Rgba32>(_configuration, _canvasSize, _scope, layoutOptions: null);
        try
        {
            var target = copy.FrameCore(0);
            target.MetadataCore = frame.MetadataCore.Clone();
            using (var source = frame.GetStorage().AcquireLease())
            using (var destination = target.GetStorage().AcquireLease())
            {
                if (IsInPalette(source, frame.PixelFormat))
                {
                    for (var y = 0; y < source.Height; y++)
                    {
                        _ = PixelConverter.ConvertRow(frame.PixelFormat, source.GetRowBytes(y), PixelFormat.Rgba32, destination.GetRowBytes(y));
                    }
                }
                else
                {
                    Map(source, frame.PixelFormat, destination);
                }
            }

            // The palette of the file holds the colors of the first image a sequential writer writes: later images map to them
            if (first && _sequential)
            {
                using var lease = target.GetStorage().AcquireLease();
                _colors = CollectColors(lease, PixelFormat.Rgba32) ?? throw new InvalidOperationException("The reduced image has more colors than its palette.");
                _transparent = GetTransparentColor(_colors);
            }

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private PooledBuffer RentRow(int width) => _scope.Rent(checked(width * 4), AllocationKind.Temporary, clear: false);

    private static Rgba32? GetTransparentColor(HashSet<uint> colors)
    {
        uint? smallest = null;
        foreach (var color in colors)
        {
            if (color >> 24 == 0 && (smallest is null || color < smallest))
            {
                smallest = color;
            }
        }

        return smallest is { } key ? new Rgba32((byte)(key >> 16), (byte)(key >> 8), (byte)key, 0) : null;
    }

    private static uint ToKey(Rgba32 color) => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    /// <summary>Gets the 8-bit RGBA keys of the pixels of a row, or <see langword="false"/> when a 16-bit sample has no 8-bit value.</summary>
    private static bool TryReadColors(ReadOnlySpan<byte> row, PixelFormat pixelFormat, Span<Rgba32> rgba)
    {
        if (pixelFormat is PixelFormat.Rgba64 or PixelFormat.Gray16)
        {
            foreach (var sample in unsafe(MemoryMarshal.Cast<byte, ushort>(row)))
            {
                if ((sample >> 8) != (sample & 0xFF))
                    return false;
            }
        }

        _ = PixelConverter.ConvertRow(pixelFormat, row, PixelFormat.Rgba32, unsafe(MemoryMarshal.AsBytes(rgba)));
        return true;
    }

    /// <summary>Gets the exact colors of a leased image, or <see langword="null"/> when it has more than 256 or a 16-bit sample has no 8-bit value.</summary>
    private HashSet<uint>? CollectColors(scoped in PixelLease lease, PixelFormat pixelFormat, HashSet<uint>? colors = null)
    {
        colors ??= [];
        using var buffer = RentRow(lease.Width);
        var rgba = unsafe(MemoryMarshal.Cast<byte, Rgba32>(buffer.Span))[..lease.Width];
        for (var y = 0; y < lease.Height; y++)
        {
            if (!TryReadColors(lease.GetRowBytes(y), pixelFormat, rgba))
                return null;

            foreach (var color in rgba)
            {
                if (colors.Add(ToKey(color)) && colors.Count > ImagePalette.MaxCount)
                    return null;
            }
        }

        return colors;
    }

    private void DeterminePalette(IReadOnlyList<ImageFrame> images)
    {
        if (_options.Palette is { } palette)
        {
            _colors = [.. palette.Colors.ToArray().Select(ToKey)];
            _transparent = GetTransparentColor(_colors);
            return;
        }

        // The exact colors, when they fit
        HashSet<uint>? exact = [];
        foreach (var image in images)
        {
            using var lease = image.GetStorage().AcquireLease();
            exact = CollectColors(lease, image.PixelFormat, exact);
            if (exact is null)
                break;
        }

        if (exact is not null)
        {
            _colors = exact;
            _transparent = GetTransparentColor(exact);
            return;
        }

        using var rows = new FrameQuantization(_scope, _canvasSize.Width);
        using var quantizer = new PaletteQuantizer(_scope, translucent: true);
        quantizer.Begin(ImagePalette.MaxCount);
        foreach (var image in images)
        {
            using var lease = image.GetStorage().AcquireLease();
            rows.AddRows(quantizer, lease, image.PixelFormat, CancellationToken.None);
        }

        quantizer.BuildPalette(_options.MaxColors, includeTransparent: true);
        _colors = [];
        foreach (var key in quantizer.Palette)
        {
            _colors.Add(ToKey(ImagePalette.ToColor(key)));
        }

        _transparent = GetTransparentColor(_colors);
    }

    /// <summary>Gets a value indicating whether every color of a leased image is a palette entry.</summary>
    private bool IsInPalette(scoped in PixelLease lease, PixelFormat pixelFormat)
    {
        var colors = _colors!;
        using var buffer = RentRow(lease.Width);
        var rgba = unsafe(MemoryMarshal.Cast<byte, Rgba32>(buffer.Span))[..lease.Width];
        for (var y = 0; y < lease.Height; y++)
        {
            if (!TryReadColors(lease.GetRowBytes(y), pixelFormat, rgba))
                return false;

            foreach (var color in rgba)
            {
                if (!colors.Contains(ToKey(color)))
                    return false;
            }
        }

        return true;
    }

    private void Map(scoped in PixelLease source, PixelFormat pixelFormat, scoped in PixelLease destination)
    {
        var keys = new List<int>(_colors!.Count);
        foreach (var color in _colors.Order())
        {
            var key = PaletteQuantizer.GetKey((byte)(color >> 16), (byte)(color >> 8), (byte)color, (byte)(color >> 24));
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        using var rows = new FrameQuantization(_scope, _canvasSize.Width);
        using var quantizer = new PaletteQuantizer(_scope, translucent: true);
        using var mapper = new PaletteRowMapper(_scope, _canvasSize.Width, _options.Dithering, _options.DitherStrength);
        // The pixels are mapped in the copy: the source keeps its pixels
        for (var y = 0; y < source.Height; y++)
        {
            _ = PixelConverter.ConvertRow(pixelFormat, source.GetRowBytes(y), PixelFormat.Rgba32, destination.GetRowBytes(y));
        }

        quantizer.Begin(ImagePalette.MaxCount);
        rows.AddRows(quantizer, destination, PixelFormat.Rgba32, CancellationToken.None);
        quantizer.SetPalette([.. keys]);
        rows.MapRows(quantizer, mapper, destination, PixelFormat.Rgba32, CancellationToken.None, _transparent ?? default);
    }
}
