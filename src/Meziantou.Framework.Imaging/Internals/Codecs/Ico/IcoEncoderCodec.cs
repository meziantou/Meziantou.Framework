using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The ICO and CUR encoder registration: one single-representation file per <c>Image.Save</c>, written by one
/// session over <see cref="IcoDocumentWriter"/>. Files with several representations go
/// through <see cref="ImageCollection.Save(string, ImageEncoder?)"/>, which drives the same writer.
/// </summary>
internal sealed class IcoEncoderCodec : ImageEncoderCodec
{
    private IcoEncoderCodec(ImageFormat format) => Format = format;

    /// <summary>Gets the encoder of Windows icon files.</summary>
    public static IcoEncoderCodec Icon { get; } = new(ImageFormat.Ico);

    /// <summary>Gets the encoder of Windows cursor files.</summary>
    public static IcoEncoderCodec Cursor { get; } = new(ImageFormat.Cur);

    public override ImageFormat Format { get; }

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    private sealed class Session(ImageEncoderSessionOptions options) : ImageEncoderSession(options)
    {
        private ImageFrame? _frame;
        private bool _pending;

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            IcoDocumentWriter.ValidateSize(Options.Capabilities.Format, frame.Size);

            // Reported before any output: a DIB payload requested for 16-bit samples would discard precision
            _ = IcoDocumentWriter.UsesPngPayload(Options.Capabilities.Format, ((IcoEncoder)Options.Encoder).PayloadFormat, frame.Size, Options.PixelFormat);
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("Icon output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("Image.Save writes a single representation; use ImageCollection.Save for several.");

            _frame = frame;
            _pending = true;
        }

        public override void BeginComplete(int frameCount) => _pending = false;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (!_pending)
                return true;

            var encoder = (IcoEncoder)Options.Encoder;
            IcoDocumentWriter.Write(
                encoder,
                [new IcoDocumentWriter.Entry(_frame!, Options.PixelFormat, Options.Capabilities.SupportsFrameHotspot ? _frame!.MetadataCore.Hotspot : null)],
                output,
                Options.Scope,
                Options.Configuration,
                CancellationToken);

            _frame = null;
            _pending = false;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _frame = null;
            }

            base.Dispose(disposing);
        }
    }
}
