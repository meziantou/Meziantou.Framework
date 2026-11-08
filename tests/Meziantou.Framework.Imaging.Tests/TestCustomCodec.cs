using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// A custom codec of a test format: "MZCUSTOM", the frame count, the width and the height (one byte each), then the
/// <see cref="Rgba32"/> pixels of every frame. The encoder writes the same layout.
/// </summary>
internal sealed class TestCustomCodec : CustomImageCodec
{
    private readonly byte[] _signature;
    private readonly DecodeFunc? _decode;
    private readonly Action<CustomImageDecodeContext>? _observe;
    private readonly bool _identifyHeader;
    private readonly bool _writes;

    public TestCustomCodec(string extension = ".mzc", byte[]? signature = null, DecodeFunc? decode = null, Action<CustomImageDecodeContext>? observe = null, bool identifyHeader = true, bool writes = true)
        : base(new ImageFormatInfo("test custom", ["image/x-test-custom"], [extension]))
    {
        _signature = signature ?? Signature.ToArray();
        _decode = decode;
        _observe = observe;
        _identifyHeader = identifyHeader;
        _writes = writes;
    }

    public delegate Image DecodeFunc(ReadOnlySpan<byte> data, CustomImageDecodeContext context);

    public static ReadOnlySpan<byte> Signature => "MZCUSTOM"u8;

    public static Rgba32[][] CreateFrames(int width, int height, int count)
        => [.. Enumerable.Range(0, count).Select(frame => Enumerable.Range(0, width * height).Select(index => new Rgba32((byte)(index * 17), (byte)(frame * 40), (byte)(index + frame), (byte)(255 - index))).ToArray())];

    public static byte[] Encode(int width, int height, Rgba32[][] frames)
    {
        var data = new List<byte>();
        data.AddRange(Signature.ToArray());
        data.AddRange([(byte)frames.Length, (byte)width, (byte)height]);
        foreach (var frame in frames)
        {
            foreach (var pixel in frame)
            {
                data.AddRange([pixel.R, pixel.G, pixel.B, pixel.A]);
            }
        }

        return [.. data];
    }

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => prefix.StartsWith(_signature);

    public override ImageInfo Identify(ReadOnlySpan<byte> data, CustomImageDecodeContext context)
    {
        if (!_identifyHeader)
            return base.Identify(data, context);

        _observe?.Invoke(context);
        return CreateImageInfo(data[9], data[10], PixelFormat.Rgba32, data[8]);
    }

    public override Image Decode(ReadOnlySpan<byte> data, CustomImageDecodeContext context)
    {
        _observe?.Invoke(context);
        if (_decode is not null)
            return _decode(data, context);

        var count = data[8];
        var width = data[9];
        var height = data[10];
        var image = context.CreateImage<Rgba32>(width, height);
        var frameLength = width * height * 4;
        for (var i = 0; i < count; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            frame.ProcessPixelBytes(data.Slice(11 + (i * frameLength), frameLength), static (pixels, source) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    source.Slice(y * pixels.RowLength, pixels.RowLength).CopyTo(pixels.GetRowSpan(y));
                }
            });
        }

        return image;
    }

    public override CustomImageEncoder? CreateEncoder() => _writes ? new Encoder(FormatInfo) : null;

    /// <summary>The encoder of the test format.</summary>
    public sealed class Encoder(ImageFormatInfo formatInfo, bool fail = false) : CustomImageEncoder(formatInfo)
    {
        public int AsynchronousCalls { get; private set; }

        public override void Encode(Image image, Stream destination)
        {
            destination.Write(TestCustomCodec.Signature);
            destination.Write([(byte)image.Frames.Count, (byte)image.Width, (byte)image.Height]);
            if (fail)
                throw new UnsupportedImageFeatureException("The test encoder fails after the header.", ImageFormat.Custom, "Test");

            using var copy = image.CloneAs<Rgba32>();
            var pixels = new byte[image.Width * image.Height * 4];
            foreach (var frame in copy.Frames)
            {
                frame.CopyPixelBytesTo(pixels);
                destination.Write(pixels);
            }
        }

        public override async ValueTask EncodeAsync(Image image, Stream destination, CancellationToken cancellationToken = default)
        {
            AsynchronousCalls++;
            using var buffer = new MemoryStream();
            Encode(image, buffer);
            await destination.WriteAsync(buffer.ToArray(), cancellationToken);
        }
    }
}
