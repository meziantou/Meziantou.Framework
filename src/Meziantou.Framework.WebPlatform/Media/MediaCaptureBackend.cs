using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// The capture devices plugged into the browsing contexts (<see cref="BrowsingContextOptions.MediaCapture"/>): the cameras
/// and the microphones of <c>navigator.mediaDevices.getUserMedia()</c> and <c>enumerateDevices()</c>, and the screen of
/// <c>getDisplayMedia()</c>. The backend lists the devices and their capabilities, and produces the video frames and the
/// audio samples of the devices it starts. The library implements Media Capture and Streams on top of it
/// (https://w3c.github.io/mediacapture-main/): the permissions, the device identifiers, the constraint processing, the
/// tracks and the streams.
/// </summary>
/// <remarks>
/// <para>
/// Without a backend (the default), there is no capture device: <c>enumerateDevices()</c> returns an empty list,
/// <c>getUserMedia()</c> is rejected with a <c>NotFoundError</c> and <c>getDisplayMedia()</c> with a <c>NotAllowedError</c>.
/// </para>
/// <para>
/// A backend can be shared by several browsing contexts. Its methods are called on the thread of the event loop of the
/// browsing context that uses them; <see cref="NotifyDevicesChanged"/> can be called from any thread.
/// </para>
/// </remarks>
public abstract class MediaCaptureBackend
{
    private readonly List<WeakReference<MediaDevices>> _listeners = [];

    /// <summary>The secret mixed with the identifiers of the devices to compute the identifiers of each origin.</summary>
    internal byte[] DeviceIdSalt { get; } = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);

    /// <summary>
    /// Returns the capture devices (cameras, microphones and audio outputs). The first device of each kind is the default
    /// device of the system. The identifiers of the devices are not exposed to the documents: each origin sees its own
    /// identifiers, derived from them.
    /// </summary>
    public abstract IReadOnlyList<MediaCaptureDevice> GetDevices();

    /// <summary>
    /// Starts the capture of a device (a camera or a microphone returned by <see cref="GetDevices"/>, or a display surface
    /// returned by <see cref="SelectDisplaySurface"/>) with the settings selected by the constraints. The capture stops when
    /// the library disposes the source (when its last track stops). An exception fails the request with a
    /// <c>NotReadableError</c> (the device cannot be used, e.g. another application uses it).
    /// </summary>
    /// <param name="request">The device, its settings and the clock of the event loop.</param>
    public abstract MediaCaptureSource StartCapture(MediaCaptureRequest request);

    /// <summary>
    /// Chooses the display surface to capture for <c>getDisplayMedia()</c> (https://w3c.github.io/mediacapture-screen-share/):
    /// a browser asks the user to pick a screen, a window or a tab. Returns null when the user cancels the request, which is
    /// rejected with a <c>NotAllowedError</c>. The default implementation returns null.
    /// </summary>
    /// <param name="request">The request of the document.</param>
    public virtual DisplayCaptureSelection? SelectDisplaySurface(DisplayCaptureRequest request) => null;

    /// <summary>
    /// Notifies the documents that the list of devices changed (a device was plugged in or removed, or the default device
    /// changed): the documents that can see the change get a <c>devicechange</c> event. Can be called from any thread.
    /// </summary>
    protected void NotifyDevicesChanged()
    {
        MediaDevices[] listeners;
        lock (_listeners)
        {
            _listeners.RemoveAll(reference => !reference.TryGetTarget(out _));
            listeners = [.. _listeners.Select(reference => reference.TryGetTarget(out var target) ? target : null).OfType<MediaDevices>()];
        }

        foreach (var listener in listeners)
        {
            listener.OnDevicesChanged();
        }
    }

    internal void Register(MediaDevices mediaDevices)
    {
        lock (_listeners)
        {
            _listeners.RemoveAll(reference => !reference.TryGetTarget(out _));
            _listeners.Add(new WeakReference<MediaDevices>(mediaDevices));
        }
    }
}

/// <summary>A capture device of a <see cref="MediaCaptureBackend"/>: a camera, a microphone, an audio output or a display surface.</summary>
public sealed class MediaCaptureDevice
{
    /// <summary>Gets the identifier of the device in the backend. It must be unique among the devices of the same kind, and stable.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the kind of the device.</summary>
    public required MediaDeviceKind Kind { get; init; }

    /// <summary>Gets the label of the device (e.g. <c>External USB Webcam</c>), or an empty string.</summary>
    public string Label { get; init; } = "";

    /// <summary>
    /// Gets the identifier of the physical device that the device belongs to (e.g. the microphone and the speakers of a
    /// headset), or an empty string when the device is alone. The documents see their own identifiers, derived from it.
    /// </summary>
    public string GroupId { get; init; } = "";

    /// <summary>
    /// Gets the capabilities of the device: the values or the ranges of values of its constrainable properties (e.g.
    /// <c>facingMode</c>, <c>sampleRate</c>, <c>echoCancellation</c>, <c>zoom</c>). The <c>deviceId</c> and the <c>groupId</c>
    /// are set by the library. For a device with <see cref="VideoModes"/>, the <c>width</c>, <c>height</c>, <c>aspectRatio</c>
    /// and <c>frameRate</c> capabilities are computed from the modes.
    /// </summary>
    public MediaTrackCapabilities Capabilities { get; init; } = new();

    /// <summary>
    /// Gets the default settings of the device: the values of its constrainable properties that the constraints do not
    /// change (e.g. the native sample rate of a microphone, the <c>facingMode</c> of a camera). When several settings satisfy
    /// the constraints equally well, the closest to the default ones is selected.
    /// </summary>
    public MediaTrackSettings DefaultSettings { get; init; } = new();

    /// <summary>
    /// Gets the native video modes of a camera or a screen (its sizes and frame rates). The default mode is the one of
    /// <see cref="DefaultSettings"/> (its <c>width</c> and <c>height</c>), else the first mode. Unless the <c>resizeMode</c>
    /// capability excludes <c>crop-and-scale</c>, the library also offers the smaller sizes and frame rates, cropped and
    /// scaled down from a native mode (https://w3c.github.io/mediacapture-main/#def-constraint-resizeMode).
    /// </summary>
    public IReadOnlyList<MediaCaptureVideoMode> VideoModes { get; init; } = [];

    /// <inheritdoc/>
    public override string ToString() => $"{Kind} {Id} ({Label})";
}

/// <summary>A native video mode of a camera or a screen: its size and its frame rate.</summary>
/// <param name="Width">The width of the frames, in pixels.</param>
/// <param name="Height">The height of the frames, in pixels.</param>
/// <param name="FrameRate">The number of frames per second.</param>
public sealed record MediaCaptureVideoMode(int Width, int Height, double FrameRate);

/// <summary>A request to start a capture device, given to <see cref="MediaCaptureBackend.StartCapture"/>.</summary>
public sealed class MediaCaptureRequest
{
    internal MediaCaptureRequest(MediaCaptureDevice device, MediaTrackSettings settings, TimeProvider timeProvider)
    {
        Device = device;
        Settings = settings;
        TimeProvider = timeProvider;
    }

    /// <summary>Gets the device to start.</summary>
    public MediaCaptureDevice Device { get; }

    /// <summary>Gets the settings selected by the constraints of the request (e.g. the size of the frames, the sample rate).</summary>
    public MediaTrackSettings Settings { get; }

    /// <summary>Gets the clock of the event loop of the document (<see cref="BrowsingContextOptions.TimeProvider"/>): the timestamps of the media are measured with it.</summary>
    public TimeProvider TimeProvider { get; }
}

/// <summary>A request of <c>getDisplayMedia()</c>, given to <see cref="MediaCaptureBackend.SelectDisplaySurface"/>.</summary>
public sealed class DisplayCaptureRequest
{
    internal DisplayCaptureRequest(CallbackContext context, DisplayMediaStreamOptions options, bool audio)
    {
        Context = context;
        Options = options;
        Audio = audio;
    }

    /// <summary>Gets the context of the window that requests the capture.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the options of the request (the constraints of the video and the audio, the preferences of the surfaces to offer).</summary>
    public DisplayMediaStreamOptions Options { get; }

    /// <summary>Gets whether the audio of the surface is requested.</summary>
    public bool Audio { get; }
}

/// <summary>The display surface chosen for <c>getDisplayMedia()</c> (see <see cref="MediaCaptureBackend.SelectDisplaySurface"/>).</summary>
/// <param name="Video">The video of the surface: a device of kind <see cref="MediaDeviceKind.VideoInput"/>, whose <c>displaySurface</c> setting is <c>monitor</c>, <c>window</c> or <c>browser</c>.</param>
/// <param name="Audio">The audio of the surface (a device of kind <see cref="MediaDeviceKind.AudioInput"/>), or null when the surface has no audio or the audio was not requested.</param>
public sealed record DisplayCaptureSelection(MediaCaptureDevice Video, MediaCaptureDevice? Audio = null);

/// <summary>
/// The media produced by a capture device started by <see cref="MediaCaptureBackend.StartCapture"/>: the video frames of a
/// camera or a screen, or the audio samples of a microphone. The library pulls the media when a sink needs it (a
/// <c>video</c> element paints a frame, <c>ImageCapture.grabFrame()</c>, a Web Audio graph renders a quantum...).
/// </summary>
/// <remarks>The methods are called on the thread of the event loop of the document that started the capture.</remarks>
public abstract class MediaCaptureSource : IDisposable
{
    private Action<bool>? _mutedChanged;
    private Action? _ended;
    private volatile bool _isMuted;
    private volatile bool _isEnded;

    /// <summary>Initializes the source of a device.</summary>
    /// <param name="request">The request of <see cref="MediaCaptureBackend.StartCapture"/>.</param>
    protected MediaCaptureSource(MediaCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Device = request.Device;
        Settings = request.Settings;
    }

    /// <summary>Gets the device.</summary>
    public MediaCaptureDevice Device { get; }

    /// <summary>Gets the current settings of the device.</summary>
    public MediaTrackSettings Settings { get; private set; }

    /// <summary>Gets a value indicating whether the source is muted (it produces no media for now, e.g. the camera is covered or the operating system took the device).</summary>
    public bool IsMuted => _isMuted;

    /// <summary>Gets a value indicating whether the source ended (the device was removed): its tracks end.</summary>
    public bool IsEnded => _isEnded;

    /// <summary>
    /// Changes the settings of the device: the constraints of a track changed (<c>applyConstraints()</c>). The default
    /// implementation calls <see cref="OnSettingsChanged"/>.
    /// </summary>
    /// <param name="settings">The new settings.</param>
    internal void ApplySettings(MediaTrackSettings settings)
    {
        Settings = settings;
        OnSettingsChanged(settings);
    }

    /// <summary>Called when the settings of the device changed (see <see cref="Settings"/>).</summary>
    /// <param name="settings">The new settings.</param>
    protected virtual void OnSettingsChanged(MediaTrackSettings settings)
    {
    }

    /// <summary>
    /// Returns the video frame of a camera or a screen at a time, or null when there is no frame (an audio device, or a
    /// device that has not produced a frame yet). The size of the frame should be the one of the settings: a frame of
    /// another size is scaled.
    /// </summary>
    /// <param name="timestamp">The time since the start of the capture, measured with the clock of the request.</param>
    public virtual MediaVideoFrame? GetVideoFrame(TimeSpan timestamp) => null;

    /// <summary>
    /// Reads the audio samples of a microphone: <paramref name="destination"/> receives the interleaved samples (one per
    /// channel of the settings) of the frames from <paramref name="position"/>, at the sample rate of the settings, as
    /// 32-bit floats between -1 and 1. The default implementation writes silence.
    /// </summary>
    /// <param name="position">The index of the first sample frame (the number of frames since the start of the capture).</param>
    /// <param name="destination">The buffer to fill: a whole number of frames.</param>
    public virtual void ReadAudio(long position, Span<float> destination) => destination.Clear();

    /// <summary>Changes whether the source is muted: the tracks get a <c>mute</c> or an <c>unmute</c> event. Can be called from any thread.</summary>
    /// <param name="muted">Whether the source is muted.</param>
    protected void SetMuted(bool muted)
    {
        if (_isMuted == muted)
            return;

        _isMuted = muted;
        Volatile.Read(ref _mutedChanged)?.Invoke(muted);
    }

    /// <summary>Ends the source (e.g. the device was removed): its tracks end and get an <c>ended</c> event. Can be called from any thread.</summary>
    protected void End()
    {
        if (_isEnded)
            return;

        _isEnded = true;
        Volatile.Read(ref _ended)?.Invoke();
    }

    internal void SetCallbacks(Action<bool>? mutedChanged, Action? ended)
    {
        Volatile.Write(ref _mutedChanged, mutedChanged);
        Volatile.Write(ref _ended, ended);
    }

    /// <summary>Stops the capture: the device is released.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the device.</summary>
    /// <param name="disposing">Whether the method is called by <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
