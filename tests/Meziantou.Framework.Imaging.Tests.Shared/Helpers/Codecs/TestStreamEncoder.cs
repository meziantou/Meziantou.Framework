using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A test-only encoder registered for one output format through <see cref="ImageEncoderRegistry.Override"/>: it writes
/// <see cref="TestStreamFormat"/> behind the real writer and save entry points, so the shared lifecycle (option snapshot,
/// container constraints, call ordering, faults, flushing, atomic publication) is exercised exactly as the PNG/APNG, GIF and
/// JPEG encoders will be. It encodes in bands of <see cref="RowsPerStep"/> rows, so the writer flushes between steps.
/// </summary>
internal sealed class TestStreamEncoder(ImageFormat format) : ImageEncoderCodec
{
    private readonly List<Session> _sessions = [];

    public override ImageFormat Format => format;

    /// <summary>Gets or sets the number of rows encoded per <see cref="ImageEncoderSession.Encode"/> call.</summary>
    public int RowsPerStep { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether frames after the first are encoded as deltas of a private copy of the previous frame.</summary>
    public bool DeltaFrames { get; set; }

    /// <summary>Gets or sets the frame index and row at which encoding throws <see cref="InjectedEncoderException"/> (a late failure).</summary>
    public (int Frame, int Row)? FailAt { get; set; }

    /// <summary>Gets or sets a value indicating whether writing the trailer throws <see cref="InjectedEncoderException"/>.</summary>
    public bool FailOnComplete { get; set; }

    /// <summary>Gets or sets a codec preflight rule: frames for which it returns <see langword="true"/> are rejected by <see cref="ImageEncoderSession.ValidateFrame"/>.</summary>
    public Func<ImageFrame, bool, bool>? RejectFrame { get; set; }

    /// <summary>Gets or sets an action run before every encoding step (tests cancel there).</summary>
    public Action<Session>? BeforeStep { get; set; }

    /// <summary>Gets or sets an exception thrown when a session is created (codec-level creation failure).</summary>
    public Exception? CreationFailure { get; set; }

    /// <summary>Gets the sessions created so far.</summary>
    public IReadOnlyList<Session> Sessions => _sessions;

    /// <summary>Gets the last session.</summary>
    public Session LastSession => _sessions[^1];

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        if (CreationFailure is not null)
            throw CreationFailure;

        var session = new Session(this, options);
        _sessions.Add(session);
        return session;
    }

    internal sealed class Session(TestStreamEncoder codec, ImageEncoderSessionOptions options) : ImageEncoderSession(options)
    {
        private readonly int _rowBytes = options.CanvasSize.Width * PixelFormats.GetBytesPerPixel(options.PixelFormat);
        private PooledBuffer? _previous;
        private bool _headerWritten;
        private bool _completing;
        private bool _recordPending;
        private bool _isPoster;
        private bool _isDelta;
        private int _index;
        private int _row;
        private int _frameCount;

        /// <summary>Gets the operations, in call order.</summary>
        public List<string> Log { get; } = [];

        /// <summary>Gets the frame borrowed by the operation in progress, or <see langword="null"/> between operations.</summary>
        public ImageFrame? CurrentFrame { get; private set; }

        public bool IsDisposed { get; private set; }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            if (codec.RejectFrame?.Invoke(frame, isPoster) == true)
                throw new UnsupportedImageFeatureException("The test encoder rejects this frame.", Options.Capabilities.Format, "Test frame");
        }

        public override void BeginPosterFrame(ImageFrame frame)
        {
            Log.Add("poster");
            Start(frame, isPoster: true, index: -1);
        }

        public override void BeginFrame(ImageFrame frame, int index)
        {
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"frame {index}"));
            Start(frame, isPoster: false, index);
        }

        public override void BeginComplete(int frameCount)
        {
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"complete {frameCount}"));
            _completing = true;
            _frameCount = frameCount;
        }

        public override bool Encode(ImageOutputBuffer output)
        {
            codec.BeforeStep?.Invoke(this);
            var writer = new TestStreamFormat.OutputBufferWriter(output);
            if (_completing)
            {
                if (codec.FailOnComplete)
                    throw new InjectedEncoderException("Injected failure while writing the trailer.");

                TestStreamFormat.WriteEnd(writer, _frameCount);
                _completing = false;
                return true;
            }

            var frame = CurrentFrame ?? throw new InvalidOperationException("No operation in progress.");
            if (!_headerWritten)
            {
                // The header is written by the first operation: a poster is only known then
                _headerWritten = true;
                var flags = (byte)((Options.Capabilities.IsAnimated ? TestStreamFormat.FlagAnimated : 0) | (_isPoster ? TestStreamFormat.FlagPoster : 0));
                var comment = Options.Metadata.TextEntries.FirstOrDefault(entry => entry.Keyword == ImageTextEntry.CommentKeyword)?.Value;
                TestStreamFormat.WriteHeader(writer, Options.CanvasSize, Options.PixelFormat, Options.Capabilities.Format, flags, Options.ExpectedFrameCount ?? 0, Options.Animation?.TotalPlays, comment);
            }

            if (_recordPending)
            {
                _recordPending = false;
                TestStreamFormat.WriteFrameRecord(writer, _isPoster ? (byte)'P' : _isDelta ? (byte)'D' : (byte)'F', frame.Metadata.Duration);
            }

            using (var lease = frame.GetStorage().AcquireLease())
            {
                var end = Math.Min(Options.CanvasSize.Height, _row + codec.RowsPerStep);
                for (; _row < end; _row++)
                {
                    if (codec.FailAt is { } failAt && failAt.Frame == _index && failAt.Row == _row)
                        throw new InjectedEncoderException($"Injected failure at frame {_index}, row {_row}.");

                    var row = lease.GetRowBytes(_row);
                    var destination = output.GetSpan(_rowBytes)[.._rowBytes];
                    row.CopyTo(destination);
                    if (!_isPoster && codec.DeltaFrames)
                    {
                        // The previous frame is a private copy (bounded state): the caller's frame is only borrowed
                        var previous = (_previous ??= Options.Scope.Rent(_rowBytes * Options.CanvasSize.Height, AllocationKind.Temporary)).Span.Slice(_row * _rowBytes, _rowBytes);
                        if (_isDelta)
                        {
                            for (var i = 0; i < destination.Length; i++)
                            {
                                destination[i] ^= previous[i];
                            }
                        }

                        row.CopyTo(previous);
                    }

                    output.Advance(_rowBytes);
                }
            }

            if (_row < Options.CanvasSize.Height)
                return false;

            CurrentFrame = null;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            CurrentFrame = null;
            _previous?.Dispose();
            _previous = null;
            base.Dispose(disposing);
        }

        private void Start(ImageFrame frame, bool isPoster, int index)
        {
            CurrentFrame = frame;
            _isPoster = isPoster;
            _isDelta = !isPoster && codec.DeltaFrames && index > 0;
            _index = index;
            _row = 0;
            _recordPending = true;
        }
    }
}
