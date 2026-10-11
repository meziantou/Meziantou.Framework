using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The private canvas of an animation decoder, shared by eager loads and sequential readers:
/// encoded frame regions are blended into it, every displayed frame is a copy of it, and the disposal of each frame
/// is applied to it afterward. Caller edits of returned images never reach it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The canvas has the working layout <see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Rgba64"/> (straight alpha,
/// native-endian 16-bit samples) and starts transparent black. It is a segmented <see cref="PixelStorage"/> charged to the
/// operation scope as <see cref="AllocationKind.CompositorState"/>.
/// </description></item>
/// <item><description>
/// The restore-previous state is bounded by one frame region: it is saved when a <see cref="AnimationDisposal.RestorePrevious"/>
/// frame begins (charged as <see cref="AllocationKind.RestorePreviousState"/>) and released as soon as it is restored.
/// </description></item>
/// <item><description>
/// <see cref="AnimationBlend.Over"/> follows the exact rounding contract of <see cref="BlendOver8"/>/<see cref="BlendOver16"/>
///. The compositor is format-neutral: codecs translate their own control data (for example the
/// APNG rule that a first frame disposed to PREVIOUS is cleared) into blend and disposal values.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class AnimationCompositor : IDisposable
{
    private readonly StorageOwner _owner;
    private readonly int _bytesPerPixel;
    private readonly bool _is16Bit;
    private PixelStorage? _canvas;
    private PixelStorage? _saved;
    private Rectangle _region;
    private AnimationBlend _blend;
    private AnimationDisposal _disposal;
    private bool _frameOpen;

    /// <summary>Creates the compositor and its transparent black canvas.</summary>
    /// <param name="scope">The allocation scope charged for the canvas and the restore-previous state.</param>
    /// <param name="canvas">The canvas size (already validated against the canvas limits).</param>
    /// <param name="pixelFormat"><see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Rgba64"/>.</param>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds the live-allocation limit.</exception>
    public AnimationCompositor(AllocationScope scope, Size canvas, PixelFormat pixelFormat)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (pixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Rgba64))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "The compositor works on Rgba32 or Rgba64 pixels.");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvas.Width, nameof(canvas));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvas.Height, nameof(canvas));
        PixelFormat = pixelFormat;
        Canvas = canvas;
        _is16Bit = pixelFormat == PixelFormat.Rgba64;
        _bytesPerPixel = PixelFormats.GetBytesPerPixel(pixelFormat);
        _owner = new StorageOwner(scope, "Animation compositor");
        try
        {
            _canvas = _owner.Allocate(canvas.Width, canvas.Height, _bytesPerPixel, kind: AllocationKind.CompositorState);
        }
        catch
        {
            _owner.Dispose();
            throw;
        }
    }

    /// <summary>Gets the working pixel format of the canvas.</summary>
    public PixelFormat PixelFormat { get; }

    public Size Canvas { get; }

    /// <summary>Gets the region of the current frame.</summary>
    public Rectangle Region => _region;

    /// <summary>Gets a value indicating whether a restore-previous region is currently saved (diagnostics).</summary>
    public bool HasSavedRegion => _saved is not null;

    /// <summary>
    /// Begins a frame: remembers its region, blend and disposal, and saves the region when the disposal restores it.
    /// The region pixels are then written with <see cref="WritePixels"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The region is empty or outside the canvas (codec bug: containers validate it first).</exception>
    /// <exception cref="ImageResourceLimitException">The restore-previous state exceeds the live-allocation limit.</exception>
    public void BeginFrame(Rectangle region, AnimationBlend blend, AnimationDisposal disposal)
    {
        var canvas = GetCanvas();
        if (region.IsEmpty || region.X < 0 || region.Y < 0 || region.Right > Canvas.Width || region.Bottom > Canvas.Height)
            throw new ArgumentOutOfRangeException(nameof(region), region, "The frame region must be a non-empty rectangle inside the canvas.");

        ReleaseSaved();
        if (disposal == AnimationDisposal.RestorePrevious)
        {
            var saved = _owner.Allocate(region.Width, region.Height, _bytesPerPixel, kind: AllocationKind.RestorePreviousState);
            _saved = saved;
            using var source = canvas.AcquireLease();
            using var target = saved.AcquireLease();
            var offset = region.X * _bytesPerPixel;
            var length = region.Width * _bytesPerPixel;
            for (var y = 0; y < region.Height; y++)
            {
                source.GetRowBytes(region.Y + y).Slice(offset, length).CopyTo(target.GetRowBytes(y));
            }
        }

        _region = region;
        _blend = blend;
        _disposal = disposal;
        _frameOpen = true;
    }

    /// <summary>Leases the canvas for a batch of <see cref="WritePixels"/> calls (dispose it before returning from the parser call).</summary>
    public PixelLease LeaseCanvas() => GetCanvas().AcquireLease();

    /// <summary>
    /// Blends <paramref name="count"/> pixels of the current frame into the canvas row <paramref name="y"/>, at columns
    /// <c>x + i * step</c> (Adam7 pass rows have a step above one).
    /// </summary>
    /// <param name="canvas">A lease from <see cref="LeaseCanvas"/>.</param>
    /// <param name="x">The canvas column of the first pixel.</param>
    /// <param name="y">The canvas row.</param>
    /// <param name="step">The distance between two pixels (positive).</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="pixels">The pixels in <see cref="PixelFormat"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A pixel lies outside the frame region (codec bug).</exception>
    public void WritePixels(scoped in PixelLease canvas, int x, int y, int step, int count, ReadOnlySpan<byte> pixels)
    {
        if (!_frameOpen)
            throw new InvalidOperationException("No frame was begun.");

        if (count <= 0)
            return;

        var region = _region;
        if (step <= 0 || y < region.Y || y >= region.Bottom || x < region.X || x + ((long)(count - 1) * step) >= region.Right)
            throw new ArgumentOutOfRangeException(nameof(x), "The pixels must lie inside the frame region.");

        var bytesPerPixel = _bytesPerPixel;
        if (pixels.Length < count * bytesPerPixel)
            throw new ArgumentException("The pixel buffer is too short.", nameof(pixels));

        var row = canvas.GetRowBytes(y);
        if (_blend == AnimationBlend.Source)
        {
            if (step == 1)
            {
                pixels[..(count * bytesPerPixel)].CopyTo(row[(x * bytesPerPixel)..]);
                return;
            }

            for (var i = 0; i < count; i++)
            {
                pixels.Slice(i * bytesPerPixel, bytesPerPixel).CopyTo(row.Slice((x + (i * step)) * bytesPerPixel, bytesPerPixel));
            }

            return;
        }

        for (var i = 0; i < count; i++)
        {
            var source = pixels.Slice(i * bytesPerPixel, bytesPerPixel);
            var destination = row.Slice((x + (i * step)) * bytesPerPixel, bytesPerPixel);
            if (_is16Bit)
            {
                BlendOver16(unsafe(MemoryMarshal.Cast<byte, ushort>(source)), unsafe(MemoryMarshal.Cast<byte, ushort>(destination)));
            }
            else
            {
                BlendOver8(source, destination);
            }
        }
    }

    /// <summary>Writes every canvas row into the current image of <paramref name="sink"/> (the displayed frame).</summary>
    /// <param name="sink">A sink whose source layout is <see cref="PixelFormat"/>.</param>
    public void CopyTo(DecodedFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (sink.SourcePixelFormat != PixelFormat)
            throw new ArgumentException("The sink source layout must be the compositor pixel format.", nameof(sink));

        using var canvas = GetCanvas().AcquireLease();
        using var target = sink.LeaseCurrentFrame();
        for (var y = 0; y < Canvas.Height; y++)
        {
            sink.WriteRow(target, y, canvas.GetRowBytes(y));
        }
    }

    /// <summary>Ends the current frame by applying its disposal to the canvas (the displayed frame was already copied).</summary>
    public void EndFrame()
    {
        if (!_frameOpen)
            throw new InvalidOperationException("No frame was begun.");

        _frameOpen = false;
        var region = _region;
        switch (_disposal)
        {
            case AnimationDisposal.ClearToTransparent:
            {
                using var canvas = GetCanvas().AcquireLease();
                var offset = region.X * _bytesPerPixel;
                var length = region.Width * _bytesPerPixel;
                for (var y = region.Y; y < region.Bottom; y++)
                {
                    canvas.GetRowBytes(y).Slice(offset, length).Clear();
                }

                break;
            }

            case AnimationDisposal.RestorePrevious:
            {
                var saved = _saved ?? throw new InvalidOperationException("The restore-previous region was not saved.");
                using (var canvas = GetCanvas().AcquireLease())
                using (var source = saved.AcquireLease())
                {
                    var offset = region.X * _bytesPerPixel;
                    for (var y = 0; y < region.Height; y++)
                    {
                        source.GetRowBytes(y).CopyTo(canvas.GetRowBytes(region.Y + y)[offset..]);
                    }
                }

                ReleaseSaved();
                break;
            }
        }
    }

    public void Dispose()
    {
        _saved = null;
        _canvas = null;
        _owner.Dispose();
    }

    /// <summary>
    /// Composites one 8-bit straight-alpha RGBA pixel over another, with exact integer arithmetic.
    /// With <c>max = 255</c>, source <c>(sc, sa)</c> and canvas <c>(dc, da)</c>: <c>sa = 0</c> keeps the canvas; otherwise
    /// <c>A = sa * max + da * (max - sa)</c> (the exact output alpha times <c>max</c>), <c>a' = round(A / max)</c> (no ties:
    /// <c>max</c> is odd) and each color <c>c' = round((sc * sa * max + dc * da * (max - sa)) / A)</c>, ties upward.
    /// </summary>
    internal static void BlendOver8(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        const uint Max = byte.MaxValue;
        uint sa = source[3];
        if (sa == 0)
            return;

        uint da = destination[3];
        if (sa == Max || da == 0)
        {
            // Exact consequences of the formula: an opaque source or a transparent canvas yields the source
            source[..4].CopyTo(destination);
            return;
        }

        var inverse = Max - sa;
        var weight = da * inverse;
        var alpha = (sa * Max) + weight;
        var doubled = 2 * alpha;
        for (var c = 0; c < 3; c++)
        {
            var color = (source[c] * sa * Max) + (destination[c] * weight);
            destination[c] = (byte)(((2 * color) + alpha) / doubled);
        }

        destination[3] = (byte)((alpha + (Max / 2)) / Max);
    }

    /// <summary>Composites one 16-bit straight-alpha RGBA pixel (native-endian samples) over another: <see cref="BlendOver8"/> with <c>max = 65535</c>.</summary>
    internal static void BlendOver16(ReadOnlySpan<ushort> source, Span<ushort> destination)
    {
        const ulong Max = ushort.MaxValue;
        ulong sa = source[3];
        if (sa == 0)
            return;

        ulong da = destination[3];
        if (sa == Max || da == 0)
        {
            source[..4].CopyTo(destination);
            return;
        }

        var inverse = Max - sa;
        var weight = da * inverse;
        var alpha = (sa * Max) + weight;
        var doubled = 2 * alpha;
        for (var c = 0; c < 3; c++)
        {
            var color = (source[c] * sa * Max) + (destination[c] * weight);
            destination[c] = (ushort)(((2 * color) + alpha) / doubled);
        }

        destination[3] = (ushort)((alpha + (Max / 2)) / Max);
    }

    private PixelStorage GetCanvas() => _canvas ?? throw new ObjectDisposedException(nameof(AnimationCompositor));

    private void ReleaseSaved()
    {
        _saved?.Dispose();
        _saved = null;
    }
}
