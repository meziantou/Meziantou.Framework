using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of a media device (https://w3c.github.io/mediacapture-main/#dom-mediadevicekind).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevicekind"/>
[IdlInterface("MediaDeviceKind", "https://w3c.github.io/mediacapture-main/#dom-mediadevicekind")]
public enum MediaDeviceKind
{
    /// <summary>An audio input device (a microphone).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevicekind-audioinput"/>
    [IdlMember("audioinput", "https://w3c.github.io/mediacapture-main/#dom-mediadevicekind-audioinput")]
    AudioInput,

    /// <summary>An audio output device (speakers, headphones).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevicekind-audiooutput"/>
    [IdlMember("audiooutput", "https://w3c.github.io/mediacapture-main/#dom-mediadevicekind-audiooutput")]
    AudioOutput,

    /// <summary>A video input device (a camera).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevicekind-videoinput"/>
    [IdlMember("videoinput", "https://w3c.github.io/mediacapture-main/#dom-mediadevicekind-videoinput")]
    VideoInput,
}

/// <summary>The semantics of a <c>getUserMedia()</c> request (https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics"/>
[IdlInterface("GetUserMediaSemantics", "https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics")]
public enum GetUserMediaSemantics
{
    /// <summary>The user agent chooses the device.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics-browser-chooses"/>
    [IdlMember("browser-chooses", "https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics-browser-chooses")]
    BrowserChooses,

    /// <summary>The user chooses the device.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics-user-chooses"/>
    [IdlMember("user-chooses", "https://w3c.github.io/mediacapture-extensions/#dom-getusermediasemantics-user-chooses")]
    UserChooses,
}

/// <summary>The constraints of a <c>getUserMedia()</c> request (https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints"/>
[IdlInterface("MediaStreamConstraints", "https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints")]
public sealed class MediaStreamConstraints
{
    /// <summary>Gets whether a video track is requested, or the constraints of the video track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints-video"/>
    [IdlMember("video", "https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints-video")]
    public BooleanOrMediaTrackConstraints Video { get; init; } = false;

    /// <summary>Gets whether an audio track is requested, or the constraints of the audio track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints-audio"/>
    [IdlMember("audio", "https://w3c.github.io/mediacapture-main/#dom-mediastreamconstraints-audio")]
    public BooleanOrMediaTrackConstraints Audio { get; init; } = false;

    /// <summary>Gets the semantics of the request, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamconstraints-semantics"/>
    [IdlMember("semantics", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamconstraints-semantics")]
    public GetUserMediaSemantics? Semantics { get; init; }
}

/// <summary>
/// A media device listed by <see cref="MediaDevices.EnumerateDevices"/> (https://w3c.github.io/mediacapture-main/#device-info):
/// its kind, and when the document can see them, its identifier, its label and its group.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo"/>
[IdlInterface("MediaDeviceInfo", "https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo")]
public class MediaDeviceInfo
{
    internal MediaDeviceInfo(MediaDeviceKind kind, string deviceId, string label, string groupId)
    {
        Kind = kind;
        DeviceId = deviceId;
        Label = label;
        GroupId = groupId;
    }

    /// <summary>Gets the identifier of the device for the origin of the document, or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-deviceid"/>
    [IdlMember("deviceId", "https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-deviceid")]
    public string DeviceId { get; }

    /// <summary>Gets the kind of the device.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-kind"/>
    [IdlMember("kind", "https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-kind")]
    public MediaDeviceKind Kind { get; }

    /// <summary>Gets the label of the device, or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-label"/>
    [IdlMember("label", "https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-label")]
    public string Label { get; }

    /// <summary>Gets the identifier of the physical device of the device for the document, or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-groupid"/>
    [IdlMember("groupId", "https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-groupid")]
    public string GroupId { get; }

    internal virtual bool HasSameValues(MediaDeviceInfo other) => GetType() == other.GetType() && Kind == other.Kind && DeviceId == other.DeviceId && Label == other.Label && GroupId == other.GroupId;
}

/// <summary>An input device listed by <see cref="MediaDevices.EnumerateDevices"/>: a camera or a microphone, with its capabilities (https://w3c.github.io/mediacapture-main/#input-specific-device-info).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-inputdeviceinfo"/>
[IdlInterface("InputDeviceInfo", "https://w3c.github.io/mediacapture-main/#dom-inputdeviceinfo")]
public sealed class InputDeviceInfo : MediaDeviceInfo
{
    private readonly MediaTrackCapabilities _capabilities;

    internal InputDeviceInfo(MediaDeviceKind kind, string deviceId, string label, string groupId, MediaTrackCapabilities capabilities)
        : base(kind, deviceId, label, groupId)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    /// Returns the capabilities of the device (https://w3c.github.io/mediacapture-main/#dom-inputdeviceinfo-getcapabilities):
    /// the ones of its tracks, or an empty dictionary when the document cannot see the details of the device.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-inputdeviceinfo-getcapabilities"/>
    [IdlMember("getCapabilities", "https://w3c.github.io/mediacapture-main/#dom-inputdeviceinfo-getcapabilities")]
    public MediaTrackCapabilities GetCapabilities() => _capabilities;
}

/// <summary>The initialization dictionary of a <see cref="DeviceChangeEvent"/> (https://w3c.github.io/mediacapture-main/#dom-devicechangeeventinit).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-devicechangeeventinit"/>
[IdlInterface("DeviceChangeEventInit", "https://w3c.github.io/mediacapture-main/#dom-devicechangeeventinit")]
public sealed class DeviceChangeEventInit : EventInit
{
    /// <summary>Gets the devices.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-devicechangeeventinit-devices"/>
    [IdlMember("devices", "https://w3c.github.io/mediacapture-main/#dom-devicechangeeventinit-devices")]
    public IReadOnlyList<MediaDeviceInfo> Devices { get; init; } = [];
}

/// <summary>The event fired at <see cref="MediaDevices"/> when the devices change (<c>devicechange</c>, https://w3c.github.io/mediacapture-main/#devicechangeevent).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-devicechangeevent"/>
[IdlInterface("DeviceChangeEvent", "https://w3c.github.io/mediacapture-main/#dom-devicechangeevent")]
public sealed class DeviceChangeEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-devicechangeevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-main/#dom-devicechangeevent-constructor")]
    public DeviceChangeEvent(string type, DeviceChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Devices = [.. eventInitDict?.Devices ?? []];
        UserInsertedDevices = [];
    }

    internal DeviceChangeEvent(IReadOnlyList<MediaDeviceInfo> devices, IReadOnlyList<MediaDeviceInfo> userInsertedDevices)
        : base("devicechange")
    {
        Devices = devices;
        UserInsertedDevices = userInsertedDevices;
    }

    /// <summary>Gets the devices that the document can see.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-devicechangeevent-devices"/>
    [IdlMember("devices", "https://w3c.github.io/mediacapture-main/#dom-devicechangeevent-devices")]
    public IReadOnlyList<MediaDeviceInfo> Devices { get; }

    /// <summary>Gets the devices that were inserted by the user.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-devicechangeevent-userinserteddevices"/>
    [IdlMember("userInsertedDevices", "https://w3c.github.io/mediacapture-main/#dom-devicechangeevent-userinserteddevices")]
    public IReadOnlyList<MediaDeviceInfo> UserInsertedDevices { get; }
}

/// <summary>
/// The media devices of a window (https://w3c.github.io/mediacapture-main/#mediadevices, <c>navigator.mediaDevices</c>): the
/// cameras and the microphones of the <see cref="MediaCaptureBackend"/> of the browsing context
/// (<see cref="BrowsingContextOptions.MediaCapture"/>), and the capture of the screen.
/// </summary>
/// <remarks>
/// The capture needs a secure context, a document allowed to use the <c>camera</c> and <c>microphone</c> features
/// (<c>display-capture</c> for the screen), and the <c>camera</c> and <c>microphone</c> permissions (see
/// <see cref="BrowsingContextOptions.Permissions"/> and <see cref="BrowsingContextOptions.RequestPermission"/>). The
/// identifiers of the devices depend on the origin, and the document only sees them (and all the devices) once it captured
/// a device of the kind.
/// </remarks>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevices"/>
[IdlInterface("MediaDevices", "https://w3c.github.io/mediacapture-main/#dom-mediadevices")]
public sealed partial class MediaDevices : EventTarget
{
    private readonly Window _window;
    private readonly List<CaptureDeviceTrackSource> _sources = [];

    // Per document: the groupId of the devices is unique for each document (https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-groupid)
    private readonly byte[] _groupIdSalt = RandomNumberGenerator.GetBytes(16);
    private bool _canExposeCameraInfo;
    private bool _canExposeMicrophoneInfo;
    private List<MediaDeviceInfo>? _lastExposedDevices;
    private readonly List<Action> _waitingForView = [];

    internal MediaDevices(Window window)
    {
        _window = window;
        window.Options.MediaCapture?.Register(this);
    }

    internal override GlobalScope? RelevantGlobal => _window;

    private MediaCaptureBackend? Backend => _window.Options.MediaCapture;

    /// <summary>Gets or sets the event handler of the <c>devicechange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevices-ondevicechange"/>
    [IdlMember("ondevicechange", "https://w3c.github.io/mediacapture-main/#dom-mediadevices-ondevicechange")]
    public Action<Event>? OnDeviceChange
    {
        get => GetEventHandler("devicechange");
        set => SetEventHandler("devicechange", value);
    }

    /// <summary>Gets the default semantics of <see cref="GetUserMedia"/>: the user agent chooses the device (https://w3c.github.io/mediacapture-extensions/#dom-mediadevices-defaultsemantics).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediadevices-defaultsemantics"/>
    [IdlMember("defaultSemantics", "https://w3c.github.io/mediacapture-extensions/#dom-mediadevices-defaultsemantics")]
    public GetUserMediaSemantics DefaultSemantics => GetUserMediaSemantics.BrowserChooses;

    /// <summary>Returns the constrainable properties that the library supports (https://w3c.github.io/mediacapture-main/#dom-mediadevices-getsupportedconstraints).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevices-getsupportedconstraints"/>
    [IdlMember("getSupportedConstraints", "https://w3c.github.io/mediacapture-main/#dom-mediadevices-getsupportedconstraints")]
    public MediaTrackSupportedConstraints GetSupportedConstraints() => new();

    // ----------------------------------------------------------------------------------------------------------------
    // enumerateDevices() (https://w3c.github.io/mediacapture-main/#dom-mediadevices-enumeratedevices)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lists the media devices (https://w3c.github.io/mediacapture-main/#dom-mediadevices-enumeratedevices): the microphones,
    /// then the cameras, with their identifiers and labels once the document captured a device of the kind; otherwise at
    /// most one device of each kind, without details. The task completes when the document is visible.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevices-enumeratedevices"/>
    [IdlMember("enumerateDevices", "https://w3c.github.io/mediacapture-main/#dom-mediadevices-enumeratedevices")]
    public Task<IReadOnlyList<MediaDeviceInfo>> EnumerateDevices()
    {
        var result = new TaskCompletionSource<IReadOnlyList<MediaDeviceInfo>>();
        RunWhenInView(() => _window.QueueGlobalTask(() => result.TrySetResult(CreateDeviceInfoList(GetBackendDevices()))));
        return result.Task;
    }

    private IReadOnlyList<MediaCaptureDevice> GetBackendDevices() => Backend?.GetDevices() ?? [];

    /// <summary>https://w3c.github.io/mediacapture-main/#dfn-creating-a-list-of-device-info-objects</summary>
    private List<MediaDeviceInfo> CreateDeviceInfoList(IReadOnlyList<MediaCaptureDevice> devices)
    {
        var document = _window.Document;
        var microphones = new List<MediaDeviceInfo>();
        var cameras = new List<MediaDeviceInfo>();
        if (PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Microphone))
        {
            microphones.AddRange(devices.Where(d => d.Kind == MediaDeviceKind.AudioInput).Select(CreateDeviceInfo));
        }

        if (PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Camera))
        {
            cameras.AddRange(devices.Where(d => d.Kind == MediaDeviceKind.VideoInput).Select(CreateDeviceInfo));
        }

        if (!CanExposeMicrophoneInfo && microphones.Count > 1)
        {
            microphones.RemoveRange(1, microphones.Count - 1);
        }

        if (!CanExposeCameraInfo && cameras.Count > 1)
        {
            cameras.RemoveRange(1, cameras.Count - 1);
        }

        // The audio outputs are exposed with the microphones, and once the user chose them with selectAudioOutput()
        // (https://w3c.github.io/mediacapture-output/#privacy-obtaining-consent)
        var others = new List<MediaDeviceInfo>();
        if (PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.SpeakerSelection))
        {
            var outputs = devices.Where(d => d.Kind == MediaDeviceKind.AudioOutput).ToList();
            if (CanExposeMicrophoneInfo && outputs.Count > 0)
            {
                var defaultOutput = CreateDeviceInfo(outputs[0]);
                others.Add(new MediaDeviceInfo(MediaDeviceKind.AudioOutput, "default", defaultOutput.Label, defaultOutput.GroupId));
                others.AddRange(outputs.Select(CreateDeviceInfo));
            }
            else
            {
                others.AddRange(outputs.Select(CreateDeviceInfo).Where(info => _explicitlyGrantedAudioOutputDevices.Contains(info.DeviceId)));
            }
        }

        return [.. microphones, .. cameras, .. others];
    }

    /// <summary>https://w3c.github.io/mediacapture-main/#dfn-creating-a-device-info-object</summary>
    private MediaDeviceInfo CreateDeviceInfo(MediaCaptureDevice device)
    {
        var exposed = device.Kind switch
        {
            MediaDeviceKind.VideoInput => CanExposeCameraInfo,
            MediaDeviceKind.AudioInput => CanExposeMicrophoneInfo,
            _ => true,
        };

        if (device.Kind == MediaDeviceKind.AudioOutput)
            return new MediaDeviceInfo(device.Kind, GetDeviceId(device), device.Label, GetGroupId(device));

        if (!exposed)
            return new InputDeviceInfo(device.Kind, "", "", "", new MediaTrackCapabilities());

        var deviceId = GetDeviceId(device);
        var groupId = GetGroupId(device);
        var model = MediaTrackSourceModel.Create(GetKind(device), device.Capabilities, device.DefaultSettings, device.VideoModes);
        return new InputDeviceInfo(device.Kind, deviceId, device.Label, groupId, model.CreateCapabilities(deviceId, groupId));
    }

    private static MediaTrackKinds GetKind(MediaCaptureDevice device) => device.Kind == MediaDeviceKind.AudioInput ? MediaTrackKinds.Audio : MediaTrackKinds.Video;

    /// <summary>
    /// The identifier of a device for the origin of the document (https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-deviceid):
    /// the same in the documents of the same origin of the same top-level site (as the storage), and unguessable in the other ones.
    /// </summary>
    internal string GetDeviceId(MediaCaptureDevice device)
    {
        var document = _window.Document;
        var key = string.Concat(document.TopLevelOrigin.Serialize(), "\n", document.Origin.Serialize(), "\n", device.Kind.ToString(), "\n", device.Id);
        return Hash(Backend?.DeviceIdSalt ?? [], key);
    }

    /// <summary>The identifier of the group of a device for the document (https://w3c.github.io/mediacapture-main/#dom-mediadeviceinfo-groupid).</summary>
    internal string GetGroupId(MediaCaptureDevice device) => Hash(_groupIdSalt, device.GroupId.Length > 0 ? "group\n" + device.GroupId : "device\n" + device.Kind.ToString() + "\n" + device.Id);

    private static string Hash(byte[] salt, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var data = new byte[salt.Length + bytes.Length];
        salt.CopyTo(data, 0);
        bytes.CopyTo(data, salt.Length);
        return Convert.ToHexStringLower(SHA256.HashData(data));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Device information exposure (https://w3c.github.io/mediacapture-main/#device-information-exposure)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/mediacapture-main/#dfn-camera-information-can-be-exposed</summary>
    private bool CanExposeCameraInfo => _canExposeCameraInfo || _sources.Exists(s => s.Device.Kind == MediaDeviceKind.VideoInput && !s.IsStopped);

    /// <summary>https://w3c.github.io/mediacapture-main/#dfn-microphone-information-can-be-exposed</summary>
    private bool CanExposeMicrophoneInfo => _canExposeMicrophoneInfo || _sources.Exists(s => s.Device.Kind == MediaDeviceKind.AudioInput && !s.IsStopped);

    /// <summary>https://w3c.github.io/mediacapture-main/#dfn-set-device-information-exposure</summary>
    private void SetDeviceInformationExposure(bool video, bool audio)
    {
        if (video)
        {
            _canExposeCameraInfo = true;
            if (Permissions.GetPermissionState(_window, "microphone") == PermissionState.Granted)
            {
                _canExposeMicrophoneInfo = true;
            }
        }

        if (audio)
        {
            _canExposeMicrophoneInfo = true;
            if (Permissions.GetPermissionState(_window, "camera") == PermissionState.Granted)
            {
                _canExposeCameraInfo = true;
            }
        }
    }

    /// <summary>https://w3c.github.io/mediacapture-main/#dfn-device-enumeration-can-proceed: the document is fully active and visible.</summary>
    private bool IsInView => _window.Document.IsFullyActive && !_window.Document.Hidden;

    private void RunWhenInView(Action action)
    {
        if (IsInView)
        {
            action();
            return;
        }

        _waitingForView.Add(action);
    }

    /// <summary>The document became visible: the requests that waited for it continue, and the change of the devices is reported.</summary>
    internal void OnDocumentVisible()
    {
        var waiting = _waitingForView.ToArray();
        _waitingForView.Clear();
        foreach (var action in waiting)
        {
            action();
        }

        if (_lastExposedDevices is not null)
        {
            RunDeviceChangeNotificationSteps();
        }
    }

    /// <summary>The document is unloaded: the capture of its devices stops (https://w3c.github.io/mediacapture-main/#life-cycle-and-media-flow).</summary>
    internal void OnDocumentUnloaded()
    {
        _waitingForView.Clear();
        foreach (var source in _sources.ToArray())
        {
            source.StopAllTracks();
        }
    }

    internal void OnSourceStopped(CaptureDeviceTrackSource source) => _sources.Remove(source);

    internal void QueueTask(Action action) => _window.QueueGlobalTask(action);

    /// <summary>The devices of the backend changed: https://w3c.github.io/mediacapture-main/#dfn-device-change-notification-steps. Called from any thread.</summary>
    internal void OnDevicesChanged() => _window.QueueGlobalTask(() =>
    {
        if (IsInView)
        {
            RunDeviceChangeNotificationSteps();
        }
        else
        {
            _lastExposedDevices ??= [];
        }
    });

    private void RunDeviceChangeNotificationSteps()
    {
        var lastExposedDevices = _lastExposedDevices ?? [];
        var newExposedDevices = CreateDeviceInfoList(GetBackendDevices());
        _lastExposedDevices = newExposedDevices;
        if (lastExposedDevices.Count == newExposedDevices.Count && lastExposedDevices.Zip(newExposedDevices).All(pair => pair.First.HasSameValues(pair.Second)))
            return;

        FireEvent(new DeviceChangeEvent(newExposedDevices, []));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // getUserMedia() (https://w3c.github.io/mediacapture-main/#dom-mediadevices-getusermedia)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Requests the capture of a camera and/or a microphone (https://w3c.github.io/mediacapture-main/#dom-mediadevices-getusermedia):
    /// the devices that satisfy the constraints best are started, once the user granted the permissions. The task fails with
    /// a <see cref="TypeErrorException"/> when no media is requested, a <c>NotAllowedError</c> when the permission is denied
    /// or the feature is not allowed, a <c>NotFoundError</c> when there is no device of the kind, an
    /// <see cref="OverconstrainedError"/> when no device satisfies the required constraints, and a <c>NotReadableError</c>
    /// when the device cannot be started.
    /// </summary>
    /// <param name="constraints">The media to capture and their constraints.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediadevices-getusermedia"/>
    [IdlMember("getUserMedia", "https://w3c.github.io/mediacapture-main/#dom-mediadevices-getusermedia")]
    public Task<MediaStream> GetUserMedia(MediaStreamConstraints? constraints = null)
    {
        constraints ??= new MediaStreamConstraints();
        var audio = GetRequestedConstraints(constraints.Audio, MediaTrackKinds.Audio);
        var video = GetRequestedConstraints(constraints.Video, MediaTrackKinds.Video);
        if (audio is null && video is null)
            return Task.FromException<MediaStream>(new TypeErrorException("getUserMedia() requires audio or video"));

        var document = _window.Document;
        if (!document.IsFullyActive)
            return Task.FromException<MediaStream>(DomException.InvalidState("The document is not fully active"));

        if ((audio is not null && !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Microphone))
            || (video is not null && !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Camera)))
        {
            return Task.FromException<MediaStream>(DomException.NotAllowed("The document is not allowed to use the camera or the microphone"));
        }

        var result = new TaskCompletionSource<MediaStream>();

        // The steps run in parallel, once the document is in view
        RunWhenInView(() => _window.QueueGlobalTask(() => GetUserMediaCore(audio, video, result)));
        return result.Task;
    }

    private static MediaTrackConstraints? GetRequestedConstraints(BooleanOrMediaTrackConstraints value, MediaTrackKinds kind) => value switch
    {
        { Constraints: { } constraints } => MediaTrackConstraintProcessing.RemoveConstraintsOfOtherKinds(constraints, kind),
        { Flag: true } => new MediaTrackConstraints(),
        _ => null,
    };

    private sealed record DeviceCandidate(MediaCaptureDevice Device, MediaTrackSourceModel Model, Dictionary<string, object> Settings, Dictionary<string, object> FixedValues, double FitnessDistance);

    private void GetUserMediaCore(MediaTrackConstraints? audio, MediaTrackConstraints? video, TaskCompletionSource<MediaStream> result)
    {
        if (!_window.Document.IsFullyActive)
        {
            result.TrySetException(DomException.InvalidState("The document is not fully active"));
            return;
        }

        var devices = GetBackendDevices();
        var requests = new List<(MediaTrackKinds Kind, MediaTrackConstraints Constraints, string PermissionName)>();
        if (audio is not null)
        {
            requests.Add((MediaTrackKinds.Audio, audio, "microphone"));
        }

        if (video is not null)
        {
            requests.Add((MediaTrackKinds.Video, video, "camera"));
        }

        var selected = new List<DeviceCandidate>();
        foreach (var (kind, constraints, permissionName) in requests)
        {
            // A required constraint that is not allowed to select a device
            if (MediaTrackConstraintProcessing.FindDisallowedRequiredConstraint(constraints) is { } disallowed)
            {
                result.TrySetException(new TypeErrorException($"The constraint '{disallowed}' cannot be required to select a device"));
                return;
            }

            var deviceKind = kind == MediaTrackKinds.Audio ? MediaDeviceKind.AudioInput : MediaDeviceKind.VideoInput;
            var kindDevices = devices.Where(d => d.Kind == deviceKind).ToList();
            if (kindDevices.Count == 0)
            {
                Fail(IsSpecificFailureAllowed() ? DomException.NotFound("There is no device of the requested kind") : PermissionFailure());
                return;
            }

            var candidates = new List<DeviceCandidate>();
            foreach (var device in kindDevices)
            {
                var model = MediaTrackSourceModel.Create(kind, device.Capabilities, device.DefaultSettings, device.VideoModes);
                var fixedValues = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["deviceId"] = GetDeviceId(device),
                    ["groupId"] = GetGroupId(device),
                };
                var selection = MediaTrackConstraintProcessing.SelectSettings(model, constraints, fixedValues);
                if (selection.Settings is { } settings)
                {
                    candidates.Add(new DeviceCandidate(device, model, settings, fixedValues, selection.FitnessDistance));
                }
            }

            if (candidates.Count == 0)
            {
                if (!IsSpecificFailureAllowed())
                {
                    Fail(PermissionFailure());
                    return;
                }

                // As in the browsers, the name of the constraint is given before the document captured a device (the
                // specification gives an empty name until the device information can be exposed)
                var failedConstraint = "";
                foreach (var device in kindDevices)
                {
                    var model = MediaTrackSourceModel.Create(kind, device.Capabilities, device.DefaultSettings, device.VideoModes);
                    var name = MediaTrackConstraintProcessing.FindFailedConstraint(model, constraints, new Dictionary<string, object>(StringComparer.Ordinal) { ["deviceId"] = GetDeviceId(device), ["groupId"] = GetGroupId(device) });
                    if (name.Length == 0 || (failedConstraint.Length > 0 && failedConstraint != name))
                    {
                        failedConstraint = "";
                        break;
                    }

                    failedConstraint = name;
                }

                Fail(new OverconstrainedError(failedConstraint, "No device satisfies the constraints"));
                return;
            }

            if (Permissions.GetPermissionState(_window, permissionName) == PermissionState.Denied)
            {
                Fail(PermissionFailure());
                return;
            }

            // The device with the smallest fitness distance, the default device first
            var best = candidates[0];
            foreach (var candidate in candidates)
            {
                if (candidate.FitnessDistance < best.FitnessDistance)
                {
                    best = candidate;
                }
            }

            selected.Add(best);
        }

        // The permissions are requested; the devices of the live tracks of the document are already granted
        foreach (var (kind, _, permissionName) in requests)
        {
            var hasLiveTrack = _sources.Exists(s => GetKind(s.Device) == kind && !s.IsStopped);
            if (!hasLiveTrack && Permissions.RequestPermissionToUse(_window, permissionName) != PermissionState.Granted)
            {
                Fail(PermissionFailure());
                return;
            }
        }

        SetDeviceInformationExposure(video is not null, audio is not null);

        var tracks = new List<MediaStreamTrack>();
        foreach (var (candidate, (_, constraints, _)) in selected.Zip(requests))
        {
            if (StartCapture(candidate, constraints) is not { } track)
            {
                foreach (var started in tracks)
                {
                    started.Stop();
                }

                Fail(new DomException("The device cannot be started", "NotReadableError"));
                return;
            }

            tracks.Add(track);
        }

        result.TrySetResult(new MediaStream(_window, tracks));

        bool IsSpecificFailureAllowed()
        {
            // https://w3c.github.io/mediacapture-main/#dfn-getusermedia-specific-failure-is-allowed
            if (audio is not null && Permissions.GetPermissionState(_window, "microphone") == PermissionState.Denied)
                return false;

            return video is null || Permissions.GetPermissionState(_window, "camera") != PermissionState.Denied;
        }

        void Fail(Exception exception) => result.TrySetException(exception);

        static DomException PermissionFailure() => DomException.NotAllowed("Permission denied");
    }

    /// <summary>Starts a device and creates its track, or returns null when the backend cannot start it.</summary>
    private MediaStreamTrack? StartCapture(DeviceCandidate candidate, MediaTrackConstraints constraints)
    {
        if (Backend is not { } backend)
            return null;

        var clock = _window.EventLoop.TimeProvider;
        MediaCaptureSource capture;
        try
        {
            capture = backend.StartCapture(new MediaCaptureRequest(candidate.Device, MediaTrackConstrainableProperty.CreateSettings(candidate.Settings), clock));
        }
#pragma warning disable CA1031 // A device that cannot be started fails the request with a NotReadableError
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }

        var source = new CaptureDeviceTrackSource(this, candidate.Device, capture, candidate.Model, candidate.Settings, candidate.FixedValues, clock);
        _sources.Add(source);
        return new MediaStreamTrack(_window, source, constraints, new Dictionary<string, object>(candidate.Settings, StringComparer.Ordinal));
    }
}

// Media Capture and Streams (https://w3c.github.io/mediacapture-main/#navigator-interface-extensions)
public sealed partial class Navigator
{
    private MediaDevices? _mediaDevices;

    /// <summary>Gets the media devices of the window (https://w3c.github.io/mediacapture-main/#dom-navigator-mediadevices).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-navigator-mediadevices"/>
    [IdlMember("mediaDevices", "https://w3c.github.io/mediacapture-main/#dom-navigator-mediadevices")]
    public MediaDevices MediaDevices => _mediaDevices ??= new MediaDevices(_window);

    internal MediaDevices? MediaDevicesOrNull => _mediaDevices;
}
