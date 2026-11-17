using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The entry points of <see cref="JpegXlLayeredImage"/>: the decoder in layer mode on a span or a stream, and the encoder
/// session of a still image of layers (<see cref="JpegXlEncoderCodec.CreateLayeredSession"/>) on a stream or a file
/// published atomically.
/// </summary>
/// <remarks>
/// Every layer but the last is saved, once blended, to the reference slot 0, and every layer is blended with that slot:
/// the first layer with the empty slot, transparent black.
/// </remarks>
internal static class JpegXlLayeredIO
{
    private const string Operation = "JpegXlLayeredImage";

    public static JpegXlLayeredImage Load(ReadOnlySpan<byte> data, ImageConfiguration configuration)
    {
        var context = new ImageCodecContext(configuration, Operation, CancellationToken.None);
        JpegXlTranscodeIO.EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, data, context), ImageFormat.JpegXl);
        using var parser = new LayerParser(context);
        return ImageInputPump.Run(data, parser, context).CreateImage(configuration);
    }

    public static JpegXlLayeredImage Load(Stream stream, bool ownsStream, ImageConfiguration configuration)
    {
        var context = new ImageCodecContext(configuration, Operation, CancellationToken.None);
        using var input = new ImageInputBuffer(stream, ownsStream, context);
        JpegXlTranscodeIO.EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, input, context), ImageFormat.JpegXl);
        using var parser = new LayerParser(context);
        return ImageInputPump.Run(input, parser, context).CreateImage(configuration);
    }

    public static void Save(JpegXlLayeredImage image, Stream stream, JpegXlEncoder encoder) => Write(image, stream, encoder);

    public static void Save(JpegXlLayeredImage image, string path, JpegXlEncoder encoder)
    {
        // Everything is validated before the temporary file exists
        var layers = PrepareLayers(image, encoder);
        var output = AtomicFileOutput.Create(path, asynchronous: false);
        try
        {
            Write(image, layers, output.Stream, encoder);
            output.Publish();
        }
        catch
        {
            output.Abort();
            throw;
        }
    }

    private static void Write(JpegXlLayeredImage image, Stream stream, JpegXlEncoder encoder)
    {
        Write(image, PrepareLayers(image, encoder), stream, encoder);
    }

    private static void Write(JpegXlLayeredImage image, PreparedLayers layers, Stream stream, JpegXlEncoder encoder)
    {
        var count = image.Layers.Count;

        // The layout of a layer, with its encoded name, is built when the session writes its frame and released with it:
        // the save holds at most the names of the frames it has not written yet
        var layouts = new LayerLayouts(image);
        var scope = AllocationScope.Create(image.Configuration, Operation);
        var capabilities = ImageOutputCapabilities.ForStillImage(encoder, image.Size);
        var options = new ImageEncoderSessionOptions(encoder, capabilities, image.Size, layers.PixelFormat, count, layers.Metadata, Animation: null, image.Configuration, scope);
        using var session = JpegXlEncoderCodec.CreateLayeredSession(options, layouts);
        using var output = new ImageOutputBuffer(scope);
        for (var i = 0; i < count; i++)
        {
            // A layer the encoder does not store as it is is converted while it is encoded, in the scope of the save: one
            // copy at a time counts toward the allocation limit. The session compresses the frame before Pump returns
            var source = image.Layers[i].Image.FrameCore(0);
            Image? converted = null;
            try
            {
                if (layers.Conversion is { } plan)
                {
                    converted = Image.CreateForDecoder(layers.PixelFormat, image.Configuration, source.Size, scope);
                    Image.ConvertFramePixels(source, converted.FrameCore(0), plan);
                }

                session.BeginFrame(converted?.FrameCore(0) ?? source, i);
                Pump(session, output, stream);
            }
            finally
            {
                converted?.Dispose();
            }
        }

        session.BeginComplete(count);
        Pump(session, output, stream);
    }

    private static JpegXlFrameLayout CreateLayout(JpegXlLayer layer, Size canvasSize)
    {
        var size = layer.Image.Size;
        return new JpegXlFrameLayout
        {
            HasCrop = layer.Offset != default || size != canvasSize,
            X0 = layer.Offset.X,
            Y0 = layer.Offset.Y,
            Width = size.Width,
            Height = size.Height,
            BlendMode = (int)layer.BlendMode,
            Name = layer.Name is { } text ? JpegXlLayer.StrictUtf8.GetBytes(text) : [],
        };
    }

    private static void Pump(ImageEncoderSession session, ImageOutputBuffer output, Stream stream)
    {
        while (true)
        {
            var done = session.Encode(output);
            if (done || output.ShouldFlush)
            {
                output.FlushTo(stream);
            }

            if (done)
                return;
        }
    }

    /// <summary>
    /// Validates the layers before any output exists, and gives the pixel format the session encodes: the one of the layers,
    /// its lossless widening to a pixel format the encoder stores, or 16-bit integers for floating-point samples
    /// (<see cref="ImageEncoder.FloatingPointConversion"/>), with the conversion every layer goes through.
    /// </summary>
    private static PreparedLayers PrepareLayers(JpegXlLayeredImage image, JpegXlEncoder encoder)
    {
        if (image.Layers.Count == 0)
            throw new InvalidOperationException("The JPEG XL image has no layer: add one with AddLayer.");

        var format = image.Layers[0].Image.PixelFormat;
        var frames = new List<ImageFrame>(image.Layers.Count);
        foreach (var layer in image.Layers)
        {
            // Frames or a poster added to the image of a layer after it was added would not be written
            if (layer.Image.Frames.Count != 1 || layer.Image.PosterFrame is not null)
                throw new InvalidOperationException("The image of a layer has several frames or a poster frame: a layer is a still image of one frame. Remove the other frames, or add them as layers of their own.");

            if (layer.Image.PixelFormat != format)
                throw new InvalidOperationException($"The layers of a JPEG XL image have the same pixel format: the first one is {format}, another one {layer.Image.PixelFormat}. Convert them with CloneAs.");

            EnsureSameColors(image.Metadata, layer.Image.Metadata);
            var frame = layer.Image.FrameCore(0);

            // The settings of the frame (a cursor hotspot) follow the metadata policy, like the frames of Image.Save
            MetadataWritePlan.ValidateFrameMetadata(frame.MetadataCore, ImageFormat.JpegXl, encoder.MetadataHandling);
            frames.Add(frame);
        }

        if (PixelFormats.IsCmyk(format))
            throw ImageWriterCore.CreateCmykOutputException("JPEG XL", ImageFormat.JpegXl, format);

        if (PixelFormats.IsFloatingPoint(format) && encoder.FloatingPointConversion is null)
            throw new UnsupportedImageFeatureException($"JPEG XL output does not store floating-point samples, so encoding {format} pixels would discard their range and precision. Set JpegXlEncoder.FloatingPointConversion to reduce them to 16-bit integers (Rgba64), or convert the layers explicitly with CloneAs.", ImageFormat.JpegXl, "Floating-point samples");

        var encodedFormat = PixelFormats.IsFloatingPoint(format) ? PixelFormat.Rgba64 : PixelFormatWidening.GetEncodedFormat(encoder, format);

        // The metadata are validated against the samples the encoder writes, and the samples of every layer against the
        // conversion (out-of-range or non-numeric floating-point samples), before any conversion
        var metadata = MetadataWritePlan.Create(image.Metadata, ImageFormat.JpegXl, encoder.MetadataHandling, image.Size, encodedFormat, encoder);
        PixelConversionPlan? conversion = null;
        if (encodedFormat != format)
        {
            conversion = PixelConversionPlan.Create(format, encodedFormat, encoder.FloatingPointConversion, sourceProfile: null, ImageFormat.JpegXl);
            Image.PrepareConversion(conversion, frames);
        }

        return new PreparedLayers(encodedFormat, conversion, metadata);
    }

    /// <summary>Throws when the samples of a layer are labeled with another color space than the image.</summary>
    private static void EnsureSameColors(ImageMetadata image, ImageMetadata layer)
    {
        var sameProfile = layer.IccProfile is null || (image.IccProfile is not null && layer.IccProfile.Data.Span.SequenceEqual(image.IccProfile.Data.Span));
        var sameCicp = layer.Cicp is null || Equals(layer.Cicp, image.Cicp);
        var sameTransfer = layer.TransferFunction == ColorTransferFunction.Srgb || layer.TransferFunction == image.TransferFunction;
        if (!sameProfile || !sameCicp || !sameTransfer)
            throw new UnsupportedImageFeatureException("The image of a layer has another color profile, CICP description or transfer function than JpegXlLayeredImage.Metadata, which describes the samples of every layer: convert the layer to the color space of the image, or set the metadata of the image.", ImageFormat.JpegXl, "layers in different color spaces");
    }

    /// <summary>What the session encodes: the pixel format, the conversion of the layers to it (or none), and the metadata.</summary>
    private sealed record PreparedLayers(PixelFormat PixelFormat, PixelConversionPlan? Conversion, MetadataWritePlan Metadata);

    /// <summary>Runs the structure parser with the decoder in layer mode, and keeps the layers until a layered image takes them over.</summary>
    private sealed class LayerParser : ImageParser<LayerParser>
    {
        private readonly JpegXlStructureParser _structure;
        private readonly JpegXlDecoder _decoder;
        private readonly List<JpegXlDecodedLayer> _layers = [];

        public LayerParser(ImageCodecContext context)
            : base(ImageFormat.JpegXl)
        {
            _decoder = new JpegXlDecoder(context, _layers);
            _structure = new JpegXlStructureParser(context, StructureWalk.Decode, _decoder);
        }

        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
            => _structure.Parse(buffer, isEndOfInput, out consumed);

        public override LayerParser GetResult() => this;

        public override Exception CreateTruncatedException(long position) => _structure.CreateTruncatedException(position);

        /// <summary>Creates the layered image, which takes the images of the layers over.</summary>
        public JpegXlLayeredImage CreateImage(ImageConfiguration configuration)
        {
            if (_layers.Count == 0)
                throw JpegXlFormat.Invalid("the image has no layer.");

            var size = _structure.Size;
            var result = new JpegXlLayeredImage(size.Width, size.Height, configuration);
            var metadata = _structure.Metadata;
            result.Metadata.IccProfile = metadata.IccProfile;
            result.Metadata.Cicp = metadata.Cicp;
            result.Metadata.TransferFunction = metadata.TransferFunction;
            result.Metadata.Orientation = metadata.Orientation;
            result.Metadata.ExifProfile = metadata.ExifProfile;
            result.Metadata.XmpProfile = metadata.XmpProfile;
            result.Metadata.SourceFormat = ImageFormat.JpegXl;
            foreach (var layer in _layers)
            {
                result.AdoptLayer(layer.Image, layer.Offset, layer.BlendMode, layer.Name);
            }

            _layers.Clear();
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var layer in _layers)
                {
                    layer.Image.Dispose();
                }

                _layers.Clear();
                _decoder.Dispose();
                _structure.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class LayerLayouts(JpegXlLayeredImage image) : IReadOnlyList<JpegXlFrameLayout>
    {
        public int Count => image.Layers.Count;

        public JpegXlFrameLayout this[int index] => CreateLayout(image.Layers[index], image.Size);

        public IEnumerator<JpegXlFrameLayout> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
