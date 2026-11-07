namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// The player of a media resource of a media element, created by <see cref="MediaBackend.CreatePlayer"/>: it gives the
/// metadata of the resource (the duration, the dimensions, the tracks, the buffered and seekable ranges), holds the current
/// playback position and gives the video frame to paint. The media element implements the rest: the ready states and the
/// network states, the events, the promises of <c>play()</c>, <c>loop</c>, <c>ended</c>, <c>played</c>...
/// </summary>
/// <remarks>
/// <para>
/// The playback members have a default implementation that does not decode anything: the current playback position advances
/// with the clock of the event loop (<see cref="MediaResource.TimeProvider"/>) at the playback rate while the element is
/// playing, up to the duration. A backend that only reads the metadata of the resources overrides the metadata members; a
/// backend that drives a real pipeline also overrides the playback members (<see cref="CurrentTime"/>, <see cref="Play"/>,
/// <see cref="Pause"/>, <see cref="Seek"/>, <see cref="PlaybackRate"/>, <see cref="SetVolume"/>...).
/// </para>
/// <para>
/// The media element reads the state of the player when it needs it and at regular intervals while it is playing (to fire
/// <c>timeupdate</c> and to detect the end of the resource). A player whose state changes by itself (the ready state of a
/// streamed resource, the duration, the dimensions, the tracks, an error, the end of a seek) calls
/// <see cref="NotifyStateChanged"/>, from any thread: the element then reads the state again from a task of its event loop
/// and fires the events of the changes (<c>durationchange</c>, <c>resize</c>, <c>waiting</c>, <c>canplay</c>, <c>error</c>...).
/// </para>
/// <para>The members are called on the thread of the event loop of the media element.</para>
/// </remarks>
public abstract class MediaPlayer : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private Action? _stateChanged;

    // The default playback: the position at the last change, and the timestamp of the change while the position advances
    private double _position;
    private long _startTimestamp;
    private bool _playing;
    private double _playbackRate = 1;

    /// <summary>Creates a player.</summary>
    /// <param name="timeProvider">The clock of the media timeline (<see cref="MediaResource.TimeProvider"/>), used by the default implementation of the playback members.</param>
    protected MediaPlayer(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Metadata
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the duration of the media resource in seconds (https://html.spec.whatwg.org/multipage/media.html#dom-media-duration):
    /// the time of the end of the resource on the media timeline, which starts at 0, or <see cref="double.PositiveInfinity"/>
    /// when the resource is not known to be bounded (a stream).
    /// </summary>
    public abstract double Duration { get; }

    /// <summary>Gets the natural width of the video in CSS pixels (<c>videoWidth</c>), or 0 when the resource has no video.</summary>
    public virtual int VideoWidth => 0;

    /// <summary>Gets the natural height of the video in CSS pixels (<c>videoHeight</c>), or 0 when the resource has no video.</summary>
    public virtual int VideoHeight => 0;

    /// <summary>Gets the audio tracks of the resource, in their natural order (<c>audioTracks</c>). The first one is enabled.</summary>
    public virtual IReadOnlyList<MediaTrackInfo> AudioTracks => [];

    /// <summary>Gets the video tracks of the resource, in their natural order (<c>videoTracks</c>). The first one is selected.</summary>
    public virtual IReadOnlyList<MediaTrackInfo> VideoTracks => [];

    /// <summary>
    /// Gets the text tracks of the resource and their cues (https://html.spec.whatwg.org/multipage/media.html#sourcing-in-band-text-tracks):
    /// they are exposed as the media-resource-specific text tracks of the element (<c>textTracks</c>) when the metadata of
    /// the resource is known.
    /// </summary>
    public virtual IReadOnlyList<MediaTextTrackInfo> TextTracks => [];

    /// <summary>
    /// Gets the date and time of the zero time of the media timeline, when the resource has one
    /// (https://html.spec.whatwg.org/multipage/media.html#timeline-offset, <c>getStartDate()</c>), or null.
    /// </summary>
    public virtual DateTimeOffset? StartDate => null;

    /// <summary>
    /// Gets how much of the resource is available to render the current playback position. The default is
    /// <see cref="MediaReadyState.HaveEnoughData"/>: the whole resource is given to the backend.
    /// </summary>
    public virtual MediaReadyState ReadyState => MediaReadyState.HaveEnoughData;

    /// <summary>Gets the ranges of the media timeline that are buffered (<c>buffered</c>). The default is the whole resource.</summary>
    public virtual IReadOnlyList<MediaTimeRange> Buffered => GetWholeResource();

    /// <summary>Gets the ranges of the media timeline the player can seek to (<c>seekable</c>). The default is the whole resource.</summary>
    public virtual IReadOnlyList<MediaTimeRange> Seekable => GetWholeResource();

    private MediaTimeRange[] GetWholeResource()
    {
        var duration = Duration;
        return double.IsNaN(duration) ? [] : [new MediaTimeRange(0, double.IsPositiveInfinity(duration) ? CurrentTime : duration)];
    }

    /// <summary>
    /// Gets the fatal error that stopped the player after its creation, or <see cref="MediaPlayerError.None"/>: the element
    /// then fails with <c>MEDIA_ERR_NETWORK</c> or <c>MEDIA_ERR_DECODE</c> and fires <c>error</c>.
    /// </summary>
    public virtual MediaPlayerError Error => MediaPlayerError.None;

    /// <summary>Gets the diagnostic message of <see cref="Error"/> (<c>MediaError.message</c>), or an empty string.</summary>
    public virtual string ErrorMessage => "";

    /// <summary>
    /// More bytes of a resource that arrives over time were received (the player was created from an incomplete resource,
    /// see <see cref="MediaResource.IsComplete"/>). The element reads the state of the player again afterwards (its ready
    /// state, its buffered ranges...). The default implementation does nothing.
    /// </summary>
    /// <param name="content">The bytes of the resource received so far, from its first byte.</param>
    /// <param name="isComplete">Whether <paramref name="content"/> is the entire resource.</param>
    public virtual void OnDataReceived(ReadOnlyMemory<byte> content, bool isComplete)
    {
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Playback
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the current playback position in seconds (https://html.spec.whatwg.org/multipage/media.html#current-playback-position),
    /// between 0 and <see cref="Duration"/>. The default implementation advances it with the clock at the
    /// <see cref="PlaybackRate"/> between <see cref="Play"/> and <see cref="Pause"/>.
    /// </summary>
    public virtual double CurrentTime
    {
        get
        {
            if (!_playing)
                return _position;

            var elapsed = _timeProvider.GetElapsedTime(_startTimestamp).TotalSeconds;
            return Clamp(_position + (elapsed * _playbackRate));
        }
    }

    private double Clamp(double position)
    {
        var duration = Duration;
        if (!double.IsNaN(duration) && position > duration)
        {
            position = duration;
        }

        return position < 0 ? 0 : position;
    }

    /// <summary>
    /// Starts advancing the current playback position: the media element is potentially playing
    /// (https://html.spec.whatwg.org/multipage/media.html#potentially-playing) and its document is fully active. The element
    /// handles the end of the resource (<c>ended</c>, <c>loop</c>): the position just stops at <see cref="Duration"/>.
    /// </summary>
    public virtual void Play()
    {
        if (_playing)
            return;

        _startTimestamp = _timeProvider.GetTimestamp();
        _playing = true;
    }

    /// <summary>Stops advancing the current playback position: the media element is no longer potentially playing (it is paused, it ended, it waits for data...).</summary>
    public virtual void Pause()
    {
        if (!_playing)
            return;

        _position = CurrentTime;
        _playing = false;
    }

    /// <summary>
    /// Sets the current playback position (https://html.spec.whatwg.org/multipage/media.html#dom-media-seek). The element
    /// fires <c>seeked</c> once <see cref="IsSeeking"/> is false.
    /// </summary>
    /// <param name="time">The new playback position in seconds, in one of the <see cref="Seekable"/> ranges.</param>
    public virtual void Seek(double time)
    {
        _position = Clamp(time);
        _startTimestamp = _timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Gets a value indicating whether the player is still seeking: the data of the position of the last <see cref="Seek"/>
    /// is not available yet. A player that seeks asynchronously calls <see cref="NotifyStateChanged"/> when it is done. The
    /// default is false.
    /// </summary>
    public virtual bool IsSeeking => false;

    /// <summary>
    /// Gets or sets the playback rate (<c>playbackRate</c>): the speed of the playback, as a multiple of the intrinsic speed
    /// of the resource. A negative rate plays backwards.
    /// </summary>
    public virtual double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            if (_playing)
            {
                _position = CurrentTime;
                _startTimestamp = _timeProvider.GetTimestamp();
            }

            _playbackRate = value;
        }
    }

    /// <summary>Returns whether the player supports a playback rate: the setter of <c>playbackRate</c> throws a <c>NotSupportedError</c> otherwise. The default is true.</summary>
    /// <param name="rate">The playback rate.</param>
    public virtual bool SupportsPlaybackRate(double rate) => true;

    /// <summary>Sets the volume of the audio (https://html.spec.whatwg.org/multipage/media.html#effective-media-volume). The default implementation does nothing: there is no audio output.</summary>
    /// <param name="volume">The effective media volume, from 0 (silent, e.g. the element is muted) to 1 (the loudest).</param>
    public virtual void SetVolume(double volume)
    {
    }

    /// <summary>Sets whether the pitch of the audio is preserved when the playback rate is not 1 (<c>preservesPitch</c>). The default implementation does nothing.</summary>
    /// <param name="preservesPitch">Whether the pitch is preserved.</param>
    public virtual void SetPreservesPitch(bool preservesPitch)
    {
    }

    /// <summary>Enables or disables an audio track (<c>AudioTrack.enabled</c>). The default implementation does nothing.</summary>
    /// <param name="track">A track of <see cref="AudioTracks"/>.</param>
    /// <param name="enabled">Whether the track is enabled.</param>
    public virtual void SetAudioTrackEnabled(MediaTrackInfo track, bool enabled)
    {
    }

    /// <summary>Selects the video track to play (<c>VideoTrack.selected</c>). The default implementation does nothing.</summary>
    /// <param name="track">A track of <see cref="VideoTracks"/>, or null when no video track is selected.</param>
    public virtual void SelectVideoTrack(MediaTrackInfo? track)
    {
    }

    /// <summary>
    /// Returns the frame of the video at the current playback position, painted in the box of the <c>video</c> element (with
    /// <c>object-fit</c>) and drawn by <c>drawImage()</c>, or null when there is none (the element then paints nothing, or
    /// its poster frame). Return the same instance while the frame does not change: its image is created once.
    /// </summary>
    public virtual MediaVideoFrame? GetVideoFrame() => null;

    /// <summary>
    /// Gets the number of video frames that have been displayed or dropped since the resource was loaded
    /// (<c>VideoPlaybackQuality.totalVideoFrames</c>, https://w3c.github.io/media-playback-quality/). The default is 0:
    /// nothing is decoded.
    /// </summary>
    public virtual long TotalVideoFrames => 0;

    /// <summary>Gets the number of video frames that have been dropped instead of being displayed (<c>VideoPlaybackQuality.droppedVideoFrames</c>). The default is 0.</summary>
    public virtual long DroppedVideoFrames => 0;

    // ----------------------------------------------------------------------------------------------------------------
    // Notifications
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tells the media element that the state of the player changed (the ready state, the duration, the dimensions, the
    /// ranges, an error, the end of a seek, a position that changed in a discontinuous way): the element reads it again from
    /// a task of its event loop. Can be called from any thread.
    /// </summary>
    protected void NotifyStateChanged() => Volatile.Read(ref _stateChanged)?.Invoke();

    /// <summary>Sets the callback of <see cref="NotifyStateChanged"/> (the media element that uses the player), or null.</summary>
    internal void SetStateChangedCallback(Action? callback) => Volatile.Write(ref _stateChanged, callback);

    /// <summary>Releases the resources of the player: the media element selected another resource, or it will never play again.</summary>
    public void Dispose()
    {
        SetStateChangedCallback(null);
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the player.</summary>
    /// <param name="disposing">Whether the method is called by <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>A fatal error of a <see cref="MediaPlayer"/> (https://html.spec.whatwg.org/multipage/media.html#error-codes).</summary>
public enum MediaPlayerError
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The player gave up fetching the media data after the resource was established to be usable (<c>MEDIA_ERR_NETWORK</c>).</summary>
    Network,

    /// <summary>The media data is corrupted, or cannot be decoded (<c>MEDIA_ERR_DECODE</c>).</summary>
    Decode,
}
