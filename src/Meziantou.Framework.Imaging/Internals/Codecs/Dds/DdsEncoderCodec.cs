using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The DDS encoder registration: a 2D texture of one surface, and its generated mipmaps.</summary>
internal sealed class DdsEncoderCodec : ImageEncoderCodec
{
    public static DdsEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Dds;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>Gets the name of a format for messages, such as <c>DDS BC7_UNORM</c>.</summary>
    internal static string GetFormatName(DdsTextureFormat format) => "DDS " + format switch
    {
        DdsTextureFormat.Bc6HUf16 => "BC6H_UF16",
        DdsTextureFormat.Bc6HSf16 => "BC6H_SF16",
        _ => format.ToString().Replace("Unorm", "_UNORM", StringComparison.Ordinal).Replace("Snorm", "_SNORM", StringComparison.Ordinal).Replace("Float", "_FLOAT", StringComparison.Ordinal).Replace("Srgb", "_SRGB", StringComparison.Ordinal).ToUpperInvariant(),
    };

    private sealed class Session : TextureEncoderSession
    {
        private readonly DdsTextureFormat _format;
        private readonly bool _dx10;

        public Session(ImageEncoderSessionOptions options)
            : this(options, (DdsEncoder)options.Encoder, DdsTextureWriter.ResolveFormat((DdsEncoder)options.Encoder, options.PixelFormat))
        {
        }

        private Session(ImageEncoderSessionOptions options, DdsEncoder encoder, DdsTextureFormat format)
            : base(
                options,
                DdsTextureWriter.GetTarget(format),
                encoder.Quality == TextureCompressionQuality.Thorough,
                encoder.GenerateMipmaps,
                srgbFilter: !DdsTextureWriter.IsFloatingPoint(format),
                encoder.AllowBitDepthReduction,
                encoder.AllowChannelReduction,
                GetFormatName(format),
                nameof(DdsEncoder))
        {
            _format = format;
            _dx10 = DdsTextureWriter.UsesDx10Header(format, isArray: false, encoder.HeaderVersion);
        }

        protected override IReadOnlyList<ReadOnlyMemory<byte>> CreateParts(EncodedTexture texture, out IDisposable? owner)
        {
            owner = null;
            List<ReadOnlyMemory<byte>> parts = [DdsTextureWriter.CreateHeader(texture, _format, _dx10)];
            parts.AddRange(DdsTextureWriter.GetSurfaces(texture));
            return parts;
        }
    }
}
