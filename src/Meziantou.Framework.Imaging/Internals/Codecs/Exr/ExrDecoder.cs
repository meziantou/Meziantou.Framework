using System.Buffers.Binary;
using System.Numerics;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The OpenEXR pixel sink, shared by eager loads and sequential readers: <see cref="ExrStructureParser"/> gives it the
/// chunks of the full-resolution image as they arrive (in the order of the file), and it decompresses each one and writes its
/// samples to the rows of the frame in <see cref="PixelFormat.RgbaHalf"/> (only HALF channels) or
/// <see cref="PixelFormat.RgbaFloat"/> (also when there is an alpha channel).
/// </summary>
/// <remarks>
/// <para>
/// The image is the display window: the pixels of the data window are placed at their position in it, the others stay
/// transparent black. A missing red, green or blue channel is 0, a missing alpha channel is 1, and a luminance channel
/// gives the three colors. UINT samples become the floating-point number of the same value, which must be exact. The
/// colors of an image with alpha are divided by it (<see cref="Unpremultiply"/>).
/// </para>
/// <para>
/// A chunk only covers part of the rows (a tile, or the data window of a scan line), so the rows are written in place:
/// directly in the frame when it holds the source layout, otherwise in a staging image of the source layout, converted
/// to the frame once every chunk is decoded (a conversion that tone maps needs the peak of the whole image first).
/// </para>
/// </remarks>
internal sealed class ExrDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private ExrStructureParser? _structure;
    private ExrHeader? _header;
    private DecodedFrameSink? _sink;
    private ExrChunkDecompressor? _decompressor;
    private PooledBuffer? _data;
    private Image? _staging;
    private bool _frameStarted;

    public ExrDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public void OnHeaderComplete(ExrStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _header = structure.Header;
        var format = structure.DefaultPixelFormat;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.OpenExr, structure.Size, format, format, structure.Metadata.IccProfile);
    }

    /// <summary>Decodes a chunk of the full-resolution image and writes its samples.</summary>
    /// <param name="index">The index of the chunk in the offset table.</param>
    /// <param name="stored">The stored data of the chunk.</param>
    public void OnChunk(long index, ReadOnlySpan<byte> stored)
    {
        var header = _header ?? throw new InvalidOperationException("The OpenEXR header was not parsed.");
        var sink = _sink!;
        var data = header.DataWindow;
        int x0;
        int y0;
        ExrChunkLayout layout;
        if (header.IsTiled)
        {
            var tilesX = header.GetTileCount(0, horizontal: true);
            var tx = (int)(index % tilesX);
            var ty = (int)(index / tilesX);
            x0 = data.MinX + (tx * header.TileWidth);
            y0 = data.MinY + (ty * header.TileHeight);
            var width = (int)Math.Min(header.TileWidth, data.MaxX - (long)x0 + 1);
            var height = (int)Math.Min(header.TileHeight, data.MaxY - (long)y0 + 1);
            layout = new ExrChunkLayout(header.Channels, width, 0, height);
        }
        else
        {
            x0 = data.MinX;
            y0 = (int)(data.MinY + (index * header.LinesPerChunk));
            var lines = (int)Math.Min(header.LinesPerChunk, data.MaxY - (long)y0 + 1);
            layout = new ExrChunkLayout(header.Channels, (int)data.Width, y0, lines);
        }

        _decompressor ??= new ExrChunkDecompressor(_context.Scope, header.Compression, (int)header.MaxChunkDataLength);
        _data ??= _context.Scope.Rent((int)Math.Max(header.MaxChunkDataLength, 1), AllocationKind.DecoderState, clear: false);
        var uncompressed = _data.Span[..layout.GetDataLength()];
        _decompressor.Decompress(stored, uncompressed, layout);

        var useStaging = !sink.Plan.IsIdentity;
        if (useStaging)
        {
            _staging ??= CreateStagingImage();
        }
        else if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        using var lease = useStaging ? _staging!.FrameCore(0).GetStorage().AcquireLease() : sink.LeaseCurrentFrame();
        var display = header.DisplayWindow;
        var bytesPerPixel = header.UsesHalfPixels ? 8 : 16;
        var column = (x0 - display.MinX) * bytesPerPixel;
        var lineLength = uncompressed.Length / layout.Lines;
        for (var line = 0; line < layout.Lines; line++)
        {
            var row = useStaging ? lease.GetRowBytes(y0 + line - display.MinY) : sink.GetDirectRow(lease, y0 + line - display.MinY);
            WriteLine(header, uncompressed.Slice(line * lineLength, lineLength), row.Slice(column, layout.Width * bytesPerPixel), layout.Width);
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
    }

    public void OnEnd(ExrStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var sink = _sink ?? throw new InvalidOperationException("The OpenEXR header was not parsed.");
        if (_staging is { } staging)
        {
            // The peak of a tone mapping, and the samples a conversion rejects, are those of the whole image
            var frame = staging.FrameCore(0);
            Image.PrepareConversion(sink.Plan, [frame]);
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
            using var source = frame.GetStorage().AcquireLease();
            using var destination = sink.LeaseCurrentFrame();
            for (var y = 0; y < source.Height; y++)
            {
                sink.WriteRow(destination, y, source.GetRowBytes(y));
            }
        }
        else if (!_frameStarted)
        {
            throw new InvalidOperationException("The OpenEXR image has no chunk.");
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The OpenEXR data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
        _decompressor?.Dispose();
        _decompressor = null;
        _data?.Dispose();
        _data = null;
        _staging?.Dispose();
        _staging = null;
    }

    /// <summary>Writes the samples of one line of a chunk (every channel, in name order) to pixels of the source layout.</summary>
    private static void WriteLine(ExrHeader header, ReadOnlySpan<byte> line, Span<byte> pixels, int width)
    {
        var half = header.UsesHalfPixels;
        var componentSize = half ? 2 : 4;

        // Colors 0, alpha 1, before the channels the image has are written
        if (!header.HasAlpha)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = pixels[(((x * 4) + 3) * componentSize)..];
                if (half)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(alpha, BitConverter.HalfToUInt16Bits(Half.One));
                }
                else
                {
                    BinaryPrimitives.WriteSingleLittleEndian(alpha, 1f);
                }
            }
        }

        var offset = 0;
        var channels = header.Channels;
        for (var c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            var role = header.Roles[c];
            var first = role == ExrChannelRole.Luminance ? 0 : (int)role;
            var last = role == ExrChannelRole.Luminance ? 2 : first;
            var samples = line.Slice(offset, width * channel.BytesPerSample);
            offset += samples.Length;
            for (var x = 0; x < width; x++)
            {
                for (var component = first; component <= last; component++)
                {
                    var target = pixels[(((x * 4) + component) * componentSize)..];
                    if (half)
                    {
                        // Only HALF channels: the bits are copied
                        target[0] = samples[2 * x];
                        target[1] = samples[(2 * x) + 1];
                        continue;
                    }

                    switch (channel.PixelType)
                    {
                        case ExrPixelType.Half:
                            BinaryPrimitives.WriteSingleLittleEndian(target, (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(samples[(2 * x)..])));
                            break;

                        case ExrPixelType.Float:
                            samples.Slice(4 * x, 4).CopyTo(target);
                            break;

                        default:
                            var value = BinaryPrimitives.ReadUInt32LittleEndian(samples[(4 * x)..]);
                            if (value > (1 << 24) && (value >> BitOperations.TrailingZeroCount(value)) >= (1 << 24))
                                throw ExrFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OpenEXR channel '{channel.Name}' has the UINT sample {value}, which a 32-bit floating-point number cannot hold exactly."), "UINT samples above 2^24");

                            BinaryPrimitives.WriteSingleLittleEndian(target, value);
                            break;
                    }
                }
            }
        }

        if (header.HasAlpha)
        {
            Unpremultiply(pixels, width);
        }
    }

    /// <summary>
    /// Divides the colors by the alpha of their pixel: OpenEXR colors are premultiplied by alpha (Technical Introduction,
    /// "Premultiplied vs. Un-Premultiplied Color Channels"), the pixels of the library are not. A pixel whose alpha is 0
    /// keeps its color, which is the light it emits (<see cref="ExrEncoderCodec"/> writes it back unchanged).
    /// </summary>
    internal static void Unpremultiply(Span<byte> pixels, int width)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels.Slice(x * 16, 16);
            var alpha = BinaryPrimitives.ReadSingleLittleEndian(pixel[12..]);
            if (alpha == 0)
                continue;

            for (var c = 0; c < 12; c += 4)
            {
                BinaryPrimitives.WriteSingleLittleEndian(pixel[c..], (float)(BinaryPrimitives.ReadSingleLittleEndian(pixel[c..]) / (double)alpha));
            }
        }
    }

    private Image CreateStagingImage()
    {
        var size = _structure!.Size;
        return _header!.UsesHalfPixels
            ? new Image<RgbaHalf>(_context.Configuration, size, _context.Scope, layoutOptions: null)
            : new Image<RgbaFloat>(_context.Configuration, size, _context.Scope, layoutOptions: null);
    }
}
