using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of <see cref="MediaDevices.SelectAudioOutput"/> (https://w3c.github.io/mediacapture-output/#dom-audiooutputoptions).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-output/#dom-audiooutputoptions"/>
[IdlInterface("AudioOutputOptions", "https://w3c.github.io/mediacapture-output/#dom-audiooutputoptions")]
public sealed class AudioOutputOptions
{
    /// <summary>
    /// Gets the identifier of a device the document can already use (chosen with <see cref="MediaDevices.SelectAudioOutput"/>,
    /// or an output of a captured microphone): the device is returned without asking the user when it is available. The empty
    /// string (the default) always asks the user.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-output/#dom-audiooutputoptions-deviceid"/>
    [IdlMember("deviceId", "https://w3c.github.io/mediacapture-output/#dom-audiooutputoptions-deviceid")]
    public string DeviceId { get; init; } = "";
}

// Audio Output Devices API (https://w3c.github.io/mediacapture-output/#mediadevices-extensions)
public sealed partial class MediaDevices
{
    // [[explicitlyGrantedAudioOutputDevices]] (https://w3c.github.io/mediacapture-output/#dfn-explicitlygrantedaudiooutputdevices):
    // the identifiers of the devices chosen by the user, which enumerateDevices() then lists
    private readonly HashSet<string> _explicitlyGrantedAudioOutputDevices = new(StringComparer.Ordinal);

    /// <summary>
    /// Asks the user to choose an audio output device (https://w3c.github.io/mediacapture-output/#dom-mediadevices-selectaudiooutput)
    /// with <see cref="MediaCaptureBackend.SelectAudioOutput"/>: the device is then listed by <see cref="EnumerateDevices"/>,
    /// and can be given to <see cref="HtmlMediaElement.SetSinkId"/>. The window must have transient activation. The task fails
    /// with an <c>InvalidStateError</c> <see cref="DomException"/> without user activation, a <c>NotAllowedError</c> when the
    /// <c>speaker-selection</c> feature is not allowed, the permission is denied or the user cancels, and a
    /// <c>NotFoundError</c> when there is no audio output device. A prerendering document asks once it is activated.
    /// </summary>
    /// <param name="options">The identifier of a device the document can already use.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-output/#dom-mediadevices-selectaudiooutput"/>
    [IdlMember("selectAudioOutput", "https://w3c.github.io/mediacapture-output/#dom-mediadevices-selectaudiooutput")]
    public Task<MediaDeviceInfo> SelectAudioOutput(AudioOutputOptions? options = null)
        => Document.DelayWhilePrerendering(_window, () => SelectAudioOutputAfterPrerendering(options));

    // https://wicg.github.io/nav-speculation/prerendering.html#audio-output-patch: a prerendering document asks once it is activated
    private Task<MediaDeviceInfo> SelectAudioOutputAfterPrerendering(AudioOutputOptions? options)
    {
        if (!_window.HasTransientActivation)
            return Task.FromException<MediaDeviceInfo>(DomException.InvalidState("selectAudioOutput() requires a user activation"));

        var deviceId = options?.DeviceId ?? "";
        var result = new TaskCompletionSource<MediaDeviceInfo>();
        _window.QueueGlobalTask(() =>
        {
            try
            {
                result.TrySetResult(SelectAudioOutputCore(deviceId));
            }
            catch (DomException exception)
            {
                result.TrySetException(exception);
            }
        });
        return result.Task;
    }

    private MediaDeviceInfo SelectAudioOutputCore(string deviceId)
    {
        // The permissions policy is not a step of the algorithm, but it denies the speaker-selection permission
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_window.Document, PolicyControlledFeature.SpeakerSelection) || Permissions.GetPermissionState(_window, "speaker-selection") == PermissionState.Denied)
            throw DomException.NotAllowed("The speaker-selection permission is denied");

        var outputs = GetBackendDevices().Where(device => device.Kind == MediaDeviceKind.AudioOutput).ToArray();
        if (outputs.Length == 0)
            throw DomException.NotFound("There is no audio output device");

        // A device the document can already use is returned without asking the user
        if (deviceId.Length > 0 && outputs.FirstOrDefault(device => string.Equals(GetDeviceId(device), deviceId, StringComparison.Ordinal)) is { } known && IsExposedAudioOutput(deviceId))
            return CreateDeviceInfo(known);

        var selected = Backend!.SelectAudioOutput(new AudioOutputRequest(CallbackContext.For(_window), outputs));
        if (selected is null || !outputs.Contains(selected))
            throw DomException.NotAllowed("The user did not choose an audio output device");

        var deviceInfo = CreateDeviceInfo(selected);
        _explicitlyGrantedAudioOutputDevices.Add(deviceInfo.DeviceId);
        return deviceInfo;
    }

    /// <summary>Whether <see cref="EnumerateDevices"/> lists an audio output device with this identifier (<c>default</c> included).</summary>
    internal bool IsExposedAudioOutput(string deviceId)
        => CreateDeviceInfoList(GetBackendDevices()).Exists(info => info.Kind == MediaDeviceKind.AudioOutput && string.Equals(info.DeviceId, deviceId, StringComparison.Ordinal));
}
