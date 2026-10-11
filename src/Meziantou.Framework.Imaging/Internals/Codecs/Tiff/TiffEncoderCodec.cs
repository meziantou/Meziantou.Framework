using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The TIFF encoder registration: one single-page document per <c>Image.Save</c>, written by one streaming
/// session over <see cref="TiffDocumentWriter"/>. Multi-page documents go through
/// <see cref="ImageCollection.Save(string, ImageEncoder?)"/>, which drives the same writer.
/// </summary>
internal sealed class TiffEncoderCodec : ImageEncoderCodec
{
    public static TiffEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Tiff;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    private sealed class Session : ImageEncoderSession
    {
        private readonly TiffDocumentWriter _writer;
        private ImageFrame? _frame;
        private Step _step;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _writer = new TiffDocumentWriter((TiffEncoder)options.Encoder, options.Scope);
        }

        private enum Step
        {
            None,
            Header,
            Strips,
            Complete,
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("TIFF output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("Image.Save writes a single-page TIFF document; use ImageCollection.Save for several pages.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.Complete;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                    _writer.WriteHeader(output);
                    _writer.BeginPage(Options.CanvasSize, Options.PixelFormat, Options.Metadata);
                    _step = Step.Strips;
                    return false;

                case Step.Strips:
                    if (!_writer.EncodeStrip(output, _frame!, CancellationToken))
                        return false;

                    _writer.CompletePage(output);
                    _frame = null;
                    _step = Step.None;
                    return true;

                case Step.Complete:
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No TIFF operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writer.Dispose();
                _frame = null;
            }

            base.Dispose(disposing);
        }
    }
}
