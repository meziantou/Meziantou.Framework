namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Converts the decoded planes of an image of the HEIF family to pixels, and writes them to the frame sink.</summary>
internal static class HeifFrameWriter
{
    /// <summary>Converts the planes of an image to pixels, and writes the rows of the clean aperture to the current frame of the sink.</summary>
    /// <param name="context">The operation.</param>
    /// <param name="sink">The frame sink, whose current frame receives the rows.</param>
    /// <param name="structure">The walker of the file: its color description and its pixel layout.</param>
    /// <param name="color">The planes of the color image.</param>
    /// <param name="alpha">The planes of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="codedSize">The size of the image before its clean aperture.</param>
    /// <param name="crop">The part of the image that the clean aperture keeps.</param>
    public static void WriteFrame(ImageCodecContext context, DecodedFrameSink sink, HeifStructureParser structure, HeifPlanarImage color, HeifPlanarImage? alpha, Size codedSize, Rectangle crop)
    {
        // A 'colr' box of type 'nclx' describes the colors of the image; otherwise the bitstream does
        var nclx = structure.ContainerColorDescription;
        var format = color.Format;
        var matrix = nclx?.MatrixCoefficients ?? format.MatrixCoefficients;
        var fullRange = nclx?.FullRange ?? format.FullRange;
        var converter = new HeifYuvConverter(structure.Binding, format, matrix, fullRange, alpha?.Format, structure.PixelFormat);
        var width = codedSize.Width;
        var height = codedSize.Height;
        using var rows = Av1PooledArray<ushort>.Rent(context.Scope, 2L * width, clear: false);
        using var pixels = context.Scope.Rent(checked(width * converter.BytesPerPixel), AllocationKind.DecoderState, clear: false);
        var u = rows.Span[..width];
        var v = rows.Span.Slice(width, width);
        var pixelRow = pixels.RawBuffer.AsSpan(0, width * converter.BytesPerPixel);
        var hasChroma = color.PlaneCount > 1;
        using var target = sink.LeaseCurrentFrame();
        for (var y = 0; y < crop.Height; y++)
        {
            if ((y & 31) is 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            var sourceY = crop.Y + y;
            if (hasChroma)
            {
                converter.UpsampleChromaRow(color, 1, sourceY, width, height, u);
                converter.UpsampleChromaRow(color, 2, sourceY, width, height, v);
            }

            var alphaRow = alpha is null ? default : alpha.GetPlane(0).GetRow(sourceY)[..width];
            converter.ConvertRow(color.GetPlane(0).GetRow(sourceY)[..width], u, v, alphaRow, pixelRow);
            sink.WriteRow(target, y, pixelRow.Slice(crop.X * converter.BytesPerPixel, crop.Width * converter.BytesPerPixel));
        }
    }
}
