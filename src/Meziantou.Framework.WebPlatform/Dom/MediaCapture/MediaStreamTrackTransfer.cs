using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Reads the media of a track on the event loop of a global object, as <c>VideoFrame</c> and <c>AudioData</c> objects
/// (owned by the callbacks): each frame or block of samples of a source that pushes its media (a generator, a transferred
/// track), and the media of the other sources sampled on the clock of the event loop: the current frame at the frame rate
/// of the track (30 frames per second when it has none, 60 at most), and the audio by blocks of 10 ms. The reading stops
/// when the track ends (<c>onEnded</c> is called) or when <see cref="Stop"/> is called.
/// </summary>
internal sealed class MediaStreamTrackReader : IMediaStreamTrackSink
{
    private const double DefaultFrameRate = 30;
    private const double MaxFrameRate = 60;
    private static readonly TimeSpan AudioPeriod = TimeSpan.FromMilliseconds(10);

    private readonly GlobalScope _global;
    private readonly MediaStreamTrack _track;
    private readonly Action<VideoFrame> _onVideoFrame;
    private readonly Action<AudioData> _onAudioData;
    private readonly Action _onEnded;
    private long _startTimestamp;
    private long _audioPosition;
    private bool _started;
    private bool _stopped;

    public MediaStreamTrackReader(GlobalScope global, MediaStreamTrack track, Action<VideoFrame> onVideoFrame, Action<AudioData> onAudioData, Action onEnded)
    {
        _global = global;
        _track = track;
        _onVideoFrame = onVideoFrame;
        _onAudioData = onAudioData;
        _onEnded = onEnded;
    }

    /// <summary>Starts the reading: when the track is ended, <c>onEnded</c> is called by a task.</summary>
    public void Start()
    {
        if (_started)
            return;

        _started = true;
        if (_track.ReadyState == MediaStreamTrackState.Ended)
        {
            _global.QueueGlobalTask(() =>
            {
                if (!_stopped)
                {
                    _stopped = true;
                    _onEnded();
                }
            });
            return;
        }

        _track.AddStateChangedCallback(OnStateChanged);
        if (_track.Source.PushesMedia)
        {
            _track.AddSink(this);
        }
        else
        {
            _startTimestamp = _global.EventLoop.TimeProvider.GetTimestamp();
            _audioPosition = _track.Source.AudioPosition;
            ScheduleSample();
        }
    }

    /// <summary>Stops the reading: no callback is called afterwards.</summary>
    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _track.RemoveStateChangedCallback(OnStateChanged);
        _track.RemoveSink(this);
    }

    private void OnStateChanged()
    {
        if (_stopped || _track.ReadyState != MediaStreamTrackState.Ended)
            return;

        Stop();
        _onEnded();
    }

    void IMediaStreamTrackSink.OnVideoFrame(VideoFrame frame)
    {
        if (!_stopped && !frame.IsDetached)
        {
            _onVideoFrame(frame.Clone());
        }
    }

    void IMediaStreamTrackSink.OnAudioData(AudioData data)
    {
        if (!_stopped && !data.IsDetached)
        {
            _onAudioData(data.Clone());
        }
    }

    private double FrameRate
    {
        get
        {
            var frameRate = _track.Settings.GetValueOrDefault("frameRate") is double rate && rate > 0 ? rate : _track.Source.NativeFrameRate ?? DefaultFrameRate;
            return Math.Clamp(double.IsFinite(frameRate) ? frameRate : DefaultFrameRate, 1, MaxFrameRate);
        }
    }

    private void ScheduleSample()
    {
        var period = _track.Source.Kind == MediaTrackKinds.Audio ? AudioPeriod : TimeSpan.FromSeconds(1 / FrameRate);
        _global.EventLoop.QueueTimer(period, () => _global.RunGlobalTask(Sample));
    }

    private void Sample()
    {
        if (_stopped)
            return;

        if (_track.Source.Kind == MediaTrackKinds.Video)
        {
            SampleVideo();
        }
        else
        {
            SampleAudio();
        }

        if (!_stopped)
        {
            ScheduleSample();
        }
    }

    private void SampleVideo()
    {
        if (_track.GetVideoFrame() is not { } mediaFrame)
            return;

        var elapsed = _global.EventLoop.TimeProvider.GetElapsedTime(_startTimestamp);
        var pixels = new BitmapPixels(mediaFrame.Width, mediaFrame.Height, mediaFrame.Pixels.ToArray());
        _onVideoFrame(new VideoFrame(pixels, (long)(elapsed.Ticks / 10), (ulong)(1_000_000 / FrameRate), rotation: 0, flip: false));
    }

    private void SampleAudio()
    {
        var source = _track.Source;
        var sampleRate = Math.Max(source.AudioSampleRate, 1);
        var channels = _track.AudioChannelCount;
        var blockFrames = Math.Max(sampleRate / 100, 1);
        var available = source.AudioPosition - _audioPosition;

        // The reading is late (e.g. the event loop was busy): the audio older than a block is skipped
        if (available > sampleRate)
        {
            _audioPosition = source.AudioPosition - blockFrames;
            available = blockFrames;
        }

        var interleaved = new float[blockFrames * channels];
        while (available >= blockFrames && !_stopped)
        {
            _track.ReadAudio(_audioPosition, interleaved);
            var planar = new float[interleaved.Length];
            for (var frame = 0; frame < blockFrames; frame++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    planar[(channel * blockFrames) + frame] = interleaved[(frame * channels) + channel];
                }
            }

            var bytes = new byte[planar.Length * sizeof(float)];
            Buffer.BlockCopy(planar, 0, bytes, 0, bytes.Length);

            var data = new AudioData(new AudioDataInit
            {
                Format = AudioSampleFormat.F32Planar,
                SampleRate = sampleRate,
                NumberOfFrames = (uint)blockFrames,
                NumberOfChannels = (uint)channels,
                Timestamp = _audioPosition * 1_000_000 / sampleRate,
                Data = bytes,
            });
            _audioPosition += blockFrames;
            available -= blockFrames;
            _onAudioData(data);
        }
    }
}

/// <summary>
/// The relay of a transferred track (https://w3c.github.io/mediacapture-extensions/#transferable-mediastreamtrack), on
/// the event loop of its source: a hidden track of the source whose media and state are posted to the
/// <see cref="RemoteTrackSource"/> of the track received by another thread. The sources are not thread-safe, so they
/// keep running on the event loop where they were created. The relay stops (and its track with it) when the tracks of
/// the remote source stop, or when the event loop of the remote source is disposed (e.g. its worker is terminated); the
/// tracks of the remote source end when the source ends or its event loop is disposed.
/// </summary>
internal sealed class MediaStreamTrackRelay
{
    private readonly GlobalScope _owner;
    private readonly MediaStreamTrack _track;
    private MediaStreamTrackReader? _reader;
    private RemoteTrackSource? _remote;
    private bool _stopped;

    private MediaStreamTrackRelay(GlobalScope owner, MediaStreamTrack track)
    {
        _owner = owner;
        _track = track;
    }

    /// <summary>The event loop of the source.</summary>
    public DomEventLoop OwnerEventLoop => _owner.EventLoop;

    /// <summary>The source of the transferred track.</summary>
    public MediaStreamTrackSource Source => _track.Source;

    /// <summary>
    /// The transfer steps of a track: a hidden clone keeps the source alive until the track is received. The received track
    /// has the state of the track (enabled, content hint, constraints and settings).
    /// </summary>
    public static ITransferredData CreateTransferredData(MediaStreamTrack track)
    {
        var owner = track.RelevantGlobal ?? throw new DomException("The track cannot be transferred", "DataCloneError");
        MediaStreamTrackRelay? relay = null;
        if (track.ReadyState == MediaStreamTrackState.Live)
        {
            var clone = track.Clone();
            clone.Enabled = true;
            relay = new MediaStreamTrackRelay(owner, clone);
        }

        return new TransferredTrack(track.GetTransferredState(), relay, owner.AgentClusterKey);
    }

    /// <summary>Connects the relay to the remote source (from the thread of the remote source).</summary>
    public void Connect(RemoteTrackSource remote)
    {
        var ownerEventLoop = _owner.EventLoop;
        ownerEventLoop.QueueTask(() => Start(remote));

        // The relay stops with the event loop of the received track, and the received track ends with the event loop of the source
        _ = remote.EventLoop.WhenDisposed.ContinueWith(_ => RequestStop(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = ownerEventLoop.WhenDisposed.ContinueWith(_ => remote.PostEnded(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Stops the relay from another thread.</summary>
    public void RequestStop() => _owner.EventLoop.QueueTask(Stop);

    /// <summary>Stops the relay and its track (on the event loop of the source).</summary>
    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _reader?.Stop();
        _track.RemoveStateChangedCallback(OnStateChanged);
        _track.Stop();
    }

    private void Start(RemoteTrackSource remote)
    {
        if (_stopped || _track.ReadyState == MediaStreamTrackState.Ended)
        {
            remote.PostEnded();
            Stop();
            return;
        }

        _remote = remote;
        if (_track.Muted)
        {
            remote.PostMuted(muted: true);
        }

        _track.AddStateChangedCallback(OnStateChanged);
        _reader = new MediaStreamTrackReader(_owner, _track, remote.PostVideoFrame, remote.PostAudioData, () =>
        {
            remote.PostEnded();
            Stop();
        });
        _reader.Start();
    }

    private void OnStateChanged()
    {
        if (_remote is { } remote && _track.ReadyState == MediaStreamTrackState.Live)
        {
            remote.PostMuted(_track.Muted);
        }
    }

    /// <summary>The data of a transferred track: the receiving steps create the track in the global object of the receiver.</summary>
    private sealed class TransferredTrack(TransferredTrackState state, MediaStreamTrackRelay? relay, object? agentCluster) : ITransferredData
    {
        private MediaStreamTrack? _received;

        public object Receive(GlobalScope? target)
        {
            if (_received is not null)
                return _received;

            // The source cannot leave the agent cluster (e.g. a cross-site frame, a shared worker or a service worker)
            if (target is null || (agentCluster is not null && target.AgentClusterKey is { } targetAgentCluster && !ReferenceEquals(agentCluster, targetAgentCluster)))
            {
                relay?.RequestStop();
                throw new DomException("The track cannot be received in another agent cluster", "DataCloneError");
            }

            if (relay is null)
            {
                // An ended track: the received track is ended
                _received = MediaStreamTrack.CreateTransferredTrack(target, new RemoteTrackSource(target, relay: null, state.Kind, state.Label, state.Muted), state);
                _received.Stop();
            }
            else if (ReferenceEquals(target.EventLoop, relay.OwnerEventLoop))
            {
                // Same thread: the received track shares the source
                _received = MediaStreamTrack.CreateTransferredTrack(target, relay.Source, state);
                relay.Stop();
            }
            else
            {
                var source = new RemoteTrackSource(target, relay, state.Kind, state.Label, state.Muted);
                _received = MediaStreamTrack.CreateTransferredTrack(target, source, state);
                relay.Connect(source);
            }

            return _received;
        }
    }
}
