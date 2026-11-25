using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The focus behavior requested when a capture starts (https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior"/>
[IdlInterface("CaptureStartFocusBehavior", "https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior")]
public enum CaptureStartFocusBehavior
{
    /// <summary>The capturing application keeps the focus.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior-focus-capturing-application"/>
    [IdlMember("focus-capturing-application", "https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior-focus-capturing-application")]
    FocusCapturingApplication,

    /// <summary>The captured surface gets the focus.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior-focus-captured-surface"/>
    [IdlMember("focus-captured-surface", "https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior-focus-captured-surface")]
    FocusCapturedSurface,

    /// <summary>The focus does not change.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior-no-focus-change"/>
    [IdlMember("no-focus-change", "https://w3c.github.io/mediacapture-screen-share/#dom-capturestartfocusbehavior-no-focus-change")]
    NoFocusChange,
}

/// <summary>Whether the surface of the capturing application is offered to the user (https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum"/>
[IdlInterface("SelfCapturePreferenceEnum", "https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum")]
public enum SelfCapturePreference
{
    /// <summary>The surface is offered.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum-include"/>
    [IdlMember("include", "https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum-include")]
    Include,

    /// <summary>The surface is not offered.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum-exclude"/>
    [IdlMember("exclude", "https://w3c.github.io/mediacapture-screen-share/#dom-selfcapturepreferenceenum-exclude")]
    Exclude,
}

/// <summary>Whether the audio of the system is offered with a monitor (https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum"/>
[IdlInterface("SystemAudioPreferenceEnum", "https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum")]
public enum SystemAudioPreference
{
    /// <summary>The audio of the system is offered.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum-include"/>
    [IdlMember("include", "https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum-include")]
    Include,

    /// <summary>The audio of the system is not offered.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum-exclude"/>
    [IdlMember("exclude", "https://w3c.github.io/mediacapture-screen-share/#dom-systemaudiopreferenceenum-exclude")]
    Exclude,
}

/// <summary>Which audio is offered with a window (https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum"/>
[IdlInterface("WindowAudioPreferenceEnum", "https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum")]
public enum WindowAudioPreference
{
    /// <summary>The audio of the system.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum-system"/>
    [IdlMember("system", "https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum-system")]
    System,

    /// <summary>The audio of the window.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum-window"/>
    [IdlMember("window", "https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum-window")]
    Window,

    /// <summary>No audio.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum-exclude"/>
    [IdlMember("exclude", "https://w3c.github.io/mediacapture-screen-share/#dom-windowaudiopreferenceenum-exclude")]
    Exclude,
}

/// <summary>Whether the user can switch the captured surface during the capture (https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum"/>
[IdlInterface("SurfaceSwitchingPreferenceEnum", "https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum")]
public enum SurfaceSwitchingPreference
{
    /// <summary>The user can switch the surface.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum-include"/>
    [IdlMember("include", "https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum-include")]
    Include,

    /// <summary>The user cannot switch the surface.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum-exclude"/>
    [IdlMember("exclude", "https://w3c.github.io/mediacapture-screen-share/#dom-surfaceswitchingpreferenceenum-exclude")]
    Exclude,
}

/// <summary>Whether the monitors are offered to the user (https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum"/>
[IdlInterface("MonitorTypeSurfacesEnum", "https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum")]
public enum MonitorTypeSurfaces
{
    /// <summary>The monitors are offered.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum-include"/>
    [IdlMember("include", "https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum-include")]
    Include,

    /// <summary>The monitors are not offered.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum-exclude"/>
    [IdlMember("exclude", "https://w3c.github.io/mediacapture-screen-share/#dom-monitortypesurfacesenum-exclude")]
    Exclude,
}

/// <summary>Whether the audio is chosen by the user (https://w3c.github.io/mediacapture-screen-share/#dom-audioselectionpreferenceenum).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-audioselectionpreferenceenum"/>
[IdlInterface("AudioSelectionPreferenceEnum", "https://w3c.github.io/mediacapture-screen-share/#dom-audioselectionpreferenceenum")]
public enum AudioSelectionPreference
{
    /// <summary>The user chooses the audio.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-audioselectionpreferenceenum-preferred"/>
    [IdlMember("preferred", "https://w3c.github.io/mediacapture-screen-share/#dom-audioselectionpreferenceenum-preferred")]
    Preferred,
}

/// <summary>The options of <c>getDisplayMedia()</c> (https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions"/>
[IdlInterface("DisplayMediaStreamOptions", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions")]
public sealed class DisplayMediaStreamOptions
{
    /// <summary>Gets whether the video is requested (it must be), or the constraints of the video track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-video"/>
    [IdlMember("video", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-video")]
    public BooleanOrMediaTrackConstraints Video { get; init; } = true;

    /// <summary>Gets whether the audio is requested, or the constraints of the audio track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-audio"/>
    [IdlMember("audio", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-audio")]
    public BooleanOrMediaTrackConstraints Audio { get; init; } = false;

    /// <summary>Gets the controller of the capture, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-controller"/>
    [IdlMember("controller", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-controller")]
    public CaptureController? Controller { get; init; }

    /// <summary>Gets whether the surface of the application should be offered, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-selfbrowsersurface"/>
    [IdlMember("selfBrowserSurface", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-selfbrowsersurface")]
    public SelfCapturePreference? SelfBrowserSurface { get; init; }

    /// <summary>Gets whether the audio of the system should be offered, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-systemaudio"/>
    [IdlMember("systemAudio", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-systemaudio")]
    public SystemAudioPreference? SystemAudio { get; init; }

    /// <summary>Gets which audio should be offered with a window, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-windowaudio"/>
    [IdlMember("windowAudio", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-windowaudio")]
    public WindowAudioPreference? WindowAudio { get; init; }

    /// <summary>Gets whether the user can switch the surface, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-surfaceswitching"/>
    [IdlMember("surfaceSwitching", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-surfaceswitching")]
    public SurfaceSwitchingPreference? SurfaceSwitching { get; init; }

    /// <summary>Gets whether the monitors should be offered, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-monitortypesurfaces"/>
    [IdlMember("monitorTypeSurfaces", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-monitortypesurfaces")]
    public MonitorTypeSurfaces? MonitorTypeSurfaces { get; init; }

    /// <summary>Gets whether the user chooses the audio, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-audioselection"/>
    [IdlMember("audioSelection", "https://w3c.github.io/mediacapture-screen-share/#dom-displaymediastreamoptions-audioselection")]
    public AudioSelectionPreference? AudioSelection { get; init; }
}

/// <summary>
/// The controller of a capture of a display surface (https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller),
/// given to <c>getDisplayMedia()</c>: the application tells whether the captured surface gets the focus.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller"/>
[IdlInterface("CaptureController", "https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller")]
public sealed partial class CaptureController : EventTarget
{
    /// <summary>Creates a controller.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller-constructor")]
    public CaptureController()
    {
    }

    internal bool IsBound { get; set; }

    internal bool CaptureFailed { get; set; }

    internal MediaStreamTrackSource? Source { get; set; }

    internal string? DisplaySurfaceType { get; set; }

    internal bool FocusDecisionFinalized { get; set; }

    /// <summary>Gets the focus behavior requested by the application, or null.</summary>
    internal CaptureStartFocusBehavior? FocusBehavior { get; private set; }

    /// <summary>
    /// Sets whether the captured surface gets the focus when the capture starts
    /// (https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller-setfocusbehavior). It can be called before the
    /// capture starts, or once right after it started (until the next task) when the surface is a window or a tab.
    /// </summary>
    /// <param name="focusBehavior">The focus behavior.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c>: the capture failed or stopped, the surface is a monitor, or the decision was made.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller-setfocusbehavior"/>
    [IdlMember("setFocusBehavior", "https://w3c.github.io/mediacapture-screen-share/#dom-capturecontroller-setfocusbehavior")]
    public void SetFocusBehavior(CaptureStartFocusBehavior focusBehavior)
    {
        if (Source is null)
        {
            // As in the browsers, the behavior of a controller whose capture failed cannot be set
            if (CaptureFailed)
                throw DomException.InvalidState("The capture failed");

            FocusBehavior = focusBehavior;
            return;
        }

        if (Source.IsStopped)
            throw DomException.InvalidState("The capture is stopped");

        if (DisplaySurfaceType is not ("browser" or "window"))
            throw DomException.InvalidState("The focus behavior can only be set when capturing a window or a tab");

        if (FocusDecisionFinalized)
            throw DomException.InvalidState("The focus decision was already made");

        FocusBehavior = focusBehavior;
        FinalizeFocusDecision();
    }

    /// <summary>https://w3c.github.io/mediacapture-screen-share/#dfn-finalize-focus-decision-algorithm: the focus of the surfaces is not changed by the library.</summary>
    internal void FinalizeFocusDecision() => FocusDecisionFinalized = true;
}

// Screen Capture (https://w3c.github.io/mediacapture-screen-share/#mediadevices-additions)
public sealed partial class MediaDevices
{
    /// <summary>The properties whose maximum value cannot be lower than a floor value (https://w3c.github.io/mediacapture-screen-share/#dfn-floor-value).</summary>
    private static readonly string[] DisplayFloorProperties = ["width", "height", "frameRate", "aspectRatio"];

    /// <summary>
    /// Requests the capture of a display surface (https://w3c.github.io/mediacapture-screen-share/#dom-mediadevices-getdisplaymedia):
    /// the user chooses a screen, a window or a tab (<see cref="MediaCaptureBackend.SelectDisplaySurface"/>). The window must
    /// have transient activation, which the call consumes. The task fails with a <see cref="TypeErrorException"/> for invalid
    /// options (no video, an <c>advanced</c> or a required constraint), an <c>InvalidStateError</c> without user activation
    /// or when the controller is already used, a <c>NotAllowedError</c> when the user cancels or the feature is not allowed,
    /// and an <see cref="OverconstrainedError"/> when the constraints cannot be satisfied.
    /// </summary>
    /// <param name="options">The options of the request.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediadevices-getdisplaymedia"/>
    [IdlMember("getDisplayMedia", "https://w3c.github.io/mediacapture-screen-share/#dom-mediadevices-getdisplaymedia")]
    public Task<MediaStream> GetDisplayMedia(DisplayMediaStreamOptions? options = null)
    {
        options ??= new DisplayMediaStreamOptions();
        var controller = options.Controller;
        if (controller is not null)
        {
            if (controller.IsBound)
                return Task.FromException<MediaStream>(DomException.InvalidState("The controller is already bound to a capture"));

            controller.IsBound = true;
        }

        if (!_window.HasTransientActivation)
            return Fail(DomException.InvalidState("getDisplayMedia() requires a user activation"));

        if (options.Video is { Flag: false })
            return Fail(new TypeErrorException("getDisplayMedia() requires video"));

        foreach (var constraints in (ReadOnlySpan<MediaTrackConstraints?>)[options.Audio.Constraints, options.Video.Constraints])
        {
            if (constraints is null)
                continue;

            if (constraints.Advanced is not null)
                return Fail(new TypeErrorException("The advanced constraints are not allowed by getDisplayMedia()"));

            foreach (var (name, constraint) in MediaTrackConstraintProcessing.Normalize(constraints, bareValuesAreExact: false))
            {
                if (constraint.Min is not null || constraint.Exact is not null || constraint.ExactValues is not null)
                    return Fail(new TypeErrorException($"The constraint '{name}' cannot be required by getDisplayMedia()"));

                if (constraint.Max is { } max && max < 1 && DisplayFloorProperties.Contains(name, StringComparer.Ordinal))
                    return Fail(new OverconstrainedError(name, $"The maximum of '{name}' is lower than its floor value"));
            }
        }

        if (options.MonitorTypeSurfaces == Dom.MonitorTypeSurfaces.Exclude && options.Video.Constraints?.DisplaySurface is { } displaySurface && ContainsMonitor(displaySurface))
            return Fail(new TypeErrorException("The monitors are excluded but a monitor is requested"));

        var document = _window.Document;
        if (!document.IsFullyActive || !FocusModel.HasFocus(document))
            return Fail(DomException.InvalidState("The document is not fully active or does not have the focus"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.DisplayCapture))
            return Fail(DomException.NotAllowed("The document is not allowed to use the display-capture feature"));

        _window.ConsumeUserActivation();
        var audio = options.Audio is { Flag: true } or { Constraints: not null };
        var result = new TaskCompletionSource<MediaStream>();
        _window.QueueGlobalTask(() => GetDisplayMediaCore(options, audio, result));
        return result.Task;

        Task<MediaStream> Fail(Exception exception)
        {
            if (controller is not null)
            {
                controller.CaptureFailed = true;
            }

            return Task.FromException<MediaStream>(exception);
        }

        static bool ContainsMonitor(ConstrainDomString value) => value switch
        {
            { Bare: { } bare } => bare == "monitor",
            { BareList: { } list } => list.Contains("monitor", StringComparer.Ordinal),
            { Parameters: { } parameters } => parameters.Ideal is { } ideal && (ideal.One == "monitor" || (ideal.Many?.Contains("monitor", StringComparer.Ordinal) ?? false)),
            _ => false,
        };
    }

    private void GetDisplayMediaCore(DisplayMediaStreamOptions options, bool audio, TaskCompletionSource<MediaStream> result)
    {
        var controller = options.Controller;
        if (Permissions.GetPermissionState(_window, "display-capture") == PermissionState.Denied
            || Backend?.SelectDisplaySurface(new DisplayCaptureRequest(CallbackContext.For(_window), options, audio)) is not { } selection)
        {
            Fail(DomException.NotAllowed("Permission denied"));
            return;
        }

        var tracks = new List<MediaStreamTrack>();
        foreach (var (device, constraints) in (ReadOnlySpan<(MediaCaptureDevice?, MediaTrackConstraints)>)[(selection.Video, options.Video.Constraints ?? new()), (audio ? selection.Audio : null, options.Audio.Constraints ?? new())])
        {
            if (device is null)
                continue;

            var kind = GetKind(device);
            var model = MediaTrackSourceModel.Create(kind, device.Capabilities, device.DefaultSettings, device.VideoModes);
            var fixedValues = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["deviceId"] = GetDeviceId(device),
                ["groupId"] = GetGroupId(device),
            };
            var kindConstraints = MediaTrackConstraintProcessing.RemoveConstraintsOfOtherKinds(constraints, kind);
            var selected = MediaTrackConstraintProcessing.SelectSettings(model, kindConstraints, fixedValues);
            if (selected.Settings is not { } settings)
            {
                StopAll();
                Fail(new OverconstrainedError(selected.FailedConstraint, "The constraints cannot be satisfied"));
                return;
            }

            if (StartCapture(new DeviceCandidate(device, model, settings, fixedValues, selected.FitnessDistance), kindConstraints) is not { } track)
            {
                StopAll();
                Fail(new DomException("The display surface cannot be captured", "NotReadableError"));
                return;
            }

            tracks.Add(track);
        }

        if (controller is not null)
        {
            controller.Source = tracks[0].Source;
            controller.DisplaySurfaceType = tracks[0].Settings.GetValueOrDefault("displaySurface") as string;
        }

        result.TrySetResult(new MediaStream(_window, tracks));
        if (controller is not null)
        {
            _window.QueueGlobalTask(controller.FinalizeFocusDecision);
        }

        void StopAll()
        {
            foreach (var track in tracks)
            {
                track.Stop();
            }
        }

        void Fail(Exception exception)
        {
            if (controller is not null)
            {
                controller.CaptureFailed = true;
            }

            result.TrySetException(exception);
        }
    }
}
