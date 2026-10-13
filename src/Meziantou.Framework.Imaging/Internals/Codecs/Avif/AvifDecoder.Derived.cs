using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The images that are built from other images (ISO/IEC 23008-12, section 6.6 "Derived images"): overlays, grids whose
/// tiles are derived images, and the identity derivations of those. Each input image is rendered to straight red, green,
/// blue and alpha samples, transformed by its own properties, and composited.
/// </content>
/// <remarks>
/// <para>
/// The samples of the image have the depth of its deepest coded image: 8 bits, or 16 bits (an 8-bit input is then
/// scaled by 257, which is exact). A coded image is converted as it is when it is presented alone, with the color
/// description of its own item; gray samples become equal red, green and blue ones.
/// </para>
/// <para>
/// An overlay (<c>iovl</c>, section 6.6.2.2) fills its canvas with a color, then composites its input images in the
/// order of its <c>dimg</c> reference, each at its offset, with the source-over operator on straight alpha and the exact
/// rounding of <see cref="AnimationCompositor.BlendOver8"/>: <c>A = sa max + da (max - sa)</c>, the alpha is
/// <c>round(A / max)</c> and each color <c>round((sc sa max + dc da (max - sa)) / A)</c>, ties upward. A grid of derived
/// images copies its tiles side by side.
/// </para>
/// <para>
/// Memory: the canvas of each derivation being built on the path from the image to the input that is rendered, and
/// one input at a time, all charged to the operation scope. The coded images that are decoded count toward
/// <c>MaxTotalPixels</c> like the frames of a bitstream that are not shown.
/// </para>
/// </remarks>
internal sealed partial class AvifDecoder
{
    // Renders the image and writes the rows that its clean aperture keeps: the rotation and the mirroring of the image
    // itself are its orientation, like those of a coded image
    private void RenderDerivedImage(HeifStructureParser structure, HeifImagePlan plan, HeifImageNode root)
    {
        var max = plan.RootBitDepth > 8 ? ushort.MaxValue : byte.MaxValue;
        using var surface = RenderNode(structure, root, max, applyOperations: false);
        var crop = plan.Crop;
        var sink = _sink!;
        var bytesPerPixel = max > byte.MaxValue ? 8 : 4;
        using var pixels = _context.Scope.Rent(checked(crop.Width * bytesPerPixel), AllocationKind.DecoderState, clear: false);
        var pixelRow = pixels.RawBuffer.AsSpan(0, crop.Width * bytesPerPixel);
        using var target = sink.LeaseCurrentFrame();
        for (var y = 0; y < crop.Height; y++)
        {
            if ((y & 31) is 0)
                _context.CancellationToken.ThrowIfCancellationRequested();

            var row = surface.GetRow(crop.Y + y).Slice(4 * crop.X, 4 * crop.Width);
            if (max > byte.MaxValue)
            {
                row.CopyTo(unsafe(MemoryMarshal.Cast<byte, ushort>(pixelRow)));
            }
            else
            {
                for (var i = 0; i < row.Length; i++)
                {
                    pixelRow[i] = (byte)row[i];
                }
            }

            sink.WriteRow(target, y, pixelRow);
        }
    }

    // Renders an image of the tree: what its item reconstructs, then its own transformations
    private Surface RenderNode(HeifStructureParser structure, HeifImageNode node, int max, bool applyOperations)
    {
        _context.CancellationToken.ThrowIfCancellationRequested();
        Surface? surface = null;
        try
        {
            surface = node.Kind switch
            {
                HeifImageNodeKind.Coded => RenderCodedImage(structure, node, max),
                HeifImageNodeKind.Identity => RenderNode(structure, node.Inputs[0], max, applyOperations: true),
                HeifImageNodeKind.Grid => RenderGrid(structure, node, max),
                _ => RenderOverlay(structure, node, max),
            };

            if (applyOperations)
            {
                foreach (var (crop, orientation) in node.Operations)
                {
                    var transformed = crop is { } rectangle ? Crop(surface, rectangle) : Orient(surface, orientation);
                    surface.Dispose();
                    surface = transformed;
                }
            }

            var result = surface;
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    // A coded image and its alpha plane, converted with the color description of its item
    private Surface RenderCodedImage(HeifStructureParser structure, HeifImageNode node, int max)
    {
        using var color = DecodeImage(structure, node.Image);
        using var alpha = node.Alpha is null ? null : DecodeImage(structure, node.Alpha);
        var size = node.Image.Size;
        _context.Tracker?.ChargeHiddenPixels((long)size.Width * size.Height * (alpha is null ? 1 : 2));
        var nclx = node.Nclx;
        var format = color.Format;
        var matrix = nclx?.MatrixCoefficients ?? format.MatrixCoefficients;
        var fullRange = nclx?.FullRange ?? format.FullRange;
        var primaries = nclx?.ColorPrimaries ?? format.ColorPrimaries;
        var transfer = nclx?.TransferCharacteristics ?? format.TransferCharacteristics;
        var layout = DefaultPixelFormats.ForAvif(node.Image.IsMonochrome, alpha is not null, Math.Max(node.Image.BitDepth, node.Alpha?.BitDepth ?? 0));
        var converter = new HeifYuvConverter(AvifBinding.Instance, format, matrix, fullRange, alpha?.Format, layout, alpha is not null && node.IsPremultiplied, primaries, transfer);
        var (width, height) = (size.Width, size.Height);
        using var rows = Av1PooledArray<ushort>.Rent(_context.Scope, 2L * width, clear: false);
        using var pixels = _context.Scope.Rent(checked(width * converter.BytesPerPixel), AllocationKind.DecoderState, clear: false);
        var u = rows.Span[..width];
        var v = rows.Span.Slice(width, width);
        var pixelRow = pixels.RawBuffer.AsSpan(0, width * converter.BytesPerPixel);
        var wideRow = unsafe(MemoryMarshal.Cast<byte, ushort>(pixelRow));
        var hasChroma = color.PlaneCount > 1;

        // A sample of an 8-bit image in an image of 16-bit samples: v * 65535 / 255 = v * 257
        var scale = max > byte.MaxValue ? 257 : 1;
        Surface? surface = null;
        try
        {
            surface = new Surface(_context.Scope, width, height);
            for (var y = 0; y < height; y++)
            {
                if ((y & 31) is 0)
                    _context.CancellationToken.ThrowIfCancellationRequested();

                if (hasChroma)
                {
                    converter.UpsampleChromaRow(color, 1, y, width, height, u);
                    converter.UpsampleChromaRow(color, 2, y, width, height, v);
                }

                var alphaRow = alpha is null ? default : alpha.GetPlane(0).GetRow(y)[..width];
                converter.ConvertRow(color.GetPlane(0).GetRow(y)[..width], u, v, alphaRow, pixelRow);
                var row = surface.GetRow(y);
                for (var x = 0; x < width; x++)
                {
                    switch (layout)
                    {
                        case PixelFormat.Gray8:
                            row[4 * x] = row[(4 * x) + 1] = row[(4 * x) + 2] = (ushort)(pixelRow[x] * scale);
                            row[(4 * x) + 3] = (ushort)max;
                            break;
                        case PixelFormat.Gray16:
                            row[4 * x] = row[(4 * x) + 1] = row[(4 * x) + 2] = wideRow[x];
                            row[(4 * x) + 3] = (ushort)max;
                            break;
                        case PixelFormat.Rgb24:
                            row[4 * x] = (ushort)(pixelRow[3 * x] * scale);
                            row[(4 * x) + 1] = (ushort)(pixelRow[(3 * x) + 1] * scale);
                            row[(4 * x) + 2] = (ushort)(pixelRow[(3 * x) + 2] * scale);
                            row[(4 * x) + 3] = (ushort)max;
                            break;
                        case PixelFormat.Rgba32:
                            row[4 * x] = (ushort)(pixelRow[4 * x] * scale);
                            row[(4 * x) + 1] = (ushort)(pixelRow[(4 * x) + 1] * scale);
                            row[(4 * x) + 2] = (ushort)(pixelRow[(4 * x) + 2] * scale);
                            row[(4 * x) + 3] = (ushort)(pixelRow[(4 * x) + 3] * scale);
                            break;
                        default:
                            wideRow.Slice(4 * x, 4).CopyTo(row.Slice(4 * x, 4));
                            break;
                    }
                }
            }

            var result = surface;
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    // The tiles of a grid of derived images, side by side: they have the same size once they are transformed, and the
    // last row and column may exceed the image (the descriptor was verified when the data of the items were read)
    private Surface RenderGrid(HeifStructureParser structure, HeifImageNode node, int max)
    {
        var size = node.Image.Size;
        var tile = node.Inputs[0].OutputSize;
        Surface? surface = null;
        try
        {
            surface = new Surface(_context.Scope, size.Width, size.Height);
            for (var i = 0; i < node.Inputs.Length; i++)
            {
                using var input = RenderNode(structure, node.Inputs[i], max, applyOperations: true);
                var (left, top) = (i % node.Columns * tile.Width, i / node.Columns * tile.Height);
                var visibleWidth = Math.Min(tile.Width, size.Width - left);
                for (var y = 0; y < Math.Min(tile.Height, size.Height - top); y++)
                {
                    input.GetRow(y)[..(4 * visibleWidth)].CopyTo(surface.GetRow(top + y)[(4 * left)..]);
                }
            }

            var result = surface;
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    // ISO/IEC 23008-12, section 6.6.2.2 "Image overlay": the canvas is filled with a color, then the input images are
    // composited in their order, each at its offset, which may be outside of the canvas
    private Surface RenderOverlay(HeifStructureParser structure, HeifImageNode node, int max)
    {
        // The 16-bit values of the fill color in the samples of the image, rounded to nearest. A canvas that is filled
        // with a transparent color is transparent black: its colors would never show
        Span<ushort> fill = stackalloc ushort[4];
        if (node.Fill[3] is not 0)
        {
            // The fill color is an sRGB color: the structure parser accepted it for this image
            for (var i = 0; i < fill.Length; i++)
            {
                fill[i] = (ushort)(((node.Fill[i] * (long)max) + (ushort.MaxValue / 2)) / ushort.MaxValue);
            }
        }

        var size = node.Image.Size;
        Surface? surface = null;
        try
        {
            surface = new Surface(_context.Scope, size.Width, size.Height);
            for (var y = 0; y < size.Height; y++)
            {
                var row = surface.GetRow(y);
                for (var x = 0; x < size.Width; x++)
                {
                    fill.CopyTo(row.Slice(4 * x, 4));
                }
            }

            for (var i = 0; i < node.Inputs.Length; i++)
            {
                long left = node.Offsets[i].X;
                long top = node.Offsets[i].Y;
                using var input = RenderNode(structure, node.Inputs[i], max, applyOperations: true);

                // The part of the layer that is on the canvas
                var firstX = (int)Math.Max(0, -left);
                var firstY = (int)Math.Max(0, -top);
                var lastX = (int)Math.Min(input.Width, size.Width - left);
                var lastY = (int)Math.Min(input.Height, size.Height - top);
                for (var y = firstY; y < lastY; y++)
                {
                    var source = input.GetRow(y);
                    var destination = surface.GetRow((int)(top + y));
                    for (var x = firstX; x < lastX; x++)
                    {
                        BlendOver(source.Slice(4 * x, 4), destination.Slice((int)(4 * (left + x)), 4), max);
                    }
                }
            }

            var result = surface;
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    // Source-over on straight alpha, with the rounding contract of AnimationCompositor.BlendOver8 and BlendOver16
    private static void BlendOver(ReadOnlySpan<ushort> source, Span<ushort> destination, int max)
    {
        long sa = source[3];
        if (sa is 0)
            return;

        long da = destination[3];
        var total = (sa * max) + (da * (max - sa));
        for (var i = 0; i < 3; i++)
        {
            destination[i] = (ushort)(((2 * ((source[i] * sa * max) + (destination[i] * da * (max - sa)))) + total) / (2 * total));
        }

        destination[3] = (ushort)(((2 * total) + max) / (2 * max));
    }

    private Surface Crop(Surface source, Rectangle rectangle)
    {
        Surface? surface = null;
        try
        {
            surface = new Surface(_context.Scope, rectangle.Width, rectangle.Height);
            for (var y = 0; y < rectangle.Height; y++)
            {
                source.GetRow(rectangle.Y + y).Slice(4 * rectangle.X, 4 * rectangle.Width).CopyTo(surface.GetRow(y));
            }

            var result = surface;
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    private Surface Orient(Surface source, OrientationTransform orientation)
    {
        var size = orientation.GetOutputSize(new Size(source.Width, source.Height));
        Surface? surface = null;
        try
        {
            surface = new Surface(_context.Scope, size.Width, size.Height);
            for (var y = 0; y < size.Height; y++)
            {
                var row = surface.GetRow(y);
                for (var x = 0; x < size.Width; x++)
                {
                    // The source pixel of a pixel of the result (OrientationTransform)
                    var (u, v) = orientation.Transpose ? (y, x) : (x, y);
                    var sourceX = orientation.FlipX ? source.Width - 1 - u : u;
                    var sourceY = orientation.FlipY ? source.Height - 1 - v : v;
                    source.GetRow(sourceY).Slice(4 * sourceX, 4).CopyTo(row.Slice(4 * x, 4));
                }
            }

            var result = surface;
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    // Straight red, green, blue and alpha samples, row by row
    private sealed class Surface : IDisposable
    {
        private readonly Av1PooledArray<ushort> _samples;

        public Surface(AllocationScope scope, int width, int height)
        {
            Width = width;
            Height = height;
            _samples = Av1PooledArray<ushort>.Rent(scope, 4L * width * height);
        }

        public int Width { get; }

        public int Height { get; }

        public Span<ushort> GetRow(int y) => _samples.Span.Slice(4 * Width * y, 4 * Width);

        public void Dispose() => _samples.Dispose();
    }
}
