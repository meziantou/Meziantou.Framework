using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The KTX 2.0 encoder registration: a 2D texture of one level, and its generated mip levels.</summary>
internal sealed class Ktx2EncoderCodec : ImageEncoderCodec
{
    public static Ktx2EncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Ktx2;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>Gets the name of a format for messages, such as <c>KTX 2.0 BC7_SRGB_BLOCK</c>.</summary>
    internal static string GetFormatName(Ktx2VkFormat format) => "KTX 2.0 " + format.Name;

    private sealed class Session : TextureEncoderSession
    {
        private readonly Ktx2Encoder _encoder;
        private readonly Ktx2VkFormat _vkFormat;
        private readonly bool _srgb;

        public Session(ImageEncoderSessionOptions options)
            : this(options, (Ktx2Encoder)options.Encoder, Ktx2TextureWriter.ResolveFormat((Ktx2Encoder)options.Encoder, options.PixelFormat))
        {
        }

        private Session(ImageEncoderSessionOptions options, Ktx2Encoder encoder, Ktx2TextureFormat format)
            : this(options, encoder, format, Ktx2TextureWriter.IsSrgbTransfer(options.Metadata, Ktx2TextureWriter.IsFloatingPoint(format)))
        {
        }

        private Session(ImageEncoderSessionOptions options, Ktx2Encoder encoder, Ktx2TextureFormat format, bool srgb)
            : base(
                options,
                Ktx2TextureWriter.GetTarget(format, srgb),
                encoder.Quality == TextureCompressionQuality.Thorough,
                encoder.GenerateMipmaps,
                srgb,
                encoder.AllowBitDepthReduction,
                encoder.AllowChannelReduction,
                GetFormatName(Ktx2TextureWriter.GetVkFormat(format, srgb)),
                nameof(Ktx2Encoder))
        {
            _encoder = encoder;
            _vkFormat = Ktx2TextureWriter.GetVkFormat(format, srgb);
            _srgb = srgb;
        }

        protected override IReadOnlyList<ReadOnlyMemory<byte>> CreateParts(EncodedTexture texture, out IDisposable? owner)
        {
            var parts = Ktx2TextureWriter.Create(texture, _vkFormat, Options.Metadata, _srgb, _encoder.Supercompression, Options.Scope);
            owner = parts;
            return parts.Parts;
        }
    }
}
