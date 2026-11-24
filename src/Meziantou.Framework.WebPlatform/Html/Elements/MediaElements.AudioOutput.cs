namespace Meziantou.Framework.WebPlatform.Dom;

// Audio Output Devices API (https://w3c.github.io/mediacapture-output/#htmlmediaelement-extensions): the audio output device
// of the media elements. The library does not play the audio, so the device only changes sinkId.
public partial class HtmlMediaElement
{
    private string _sinkId = "";

    /// <summary>
    /// Gets the identifier of the audio output device of the element (a <see cref="MediaDeviceInfo.DeviceId"/> of an
    /// <c>audiooutput</c> device), or the empty string for the default device.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-output/#dom-htmlmediaelement-sinkid"/>
    [IdlMember("sinkId", "https://w3c.github.io/mediacapture-output/#dom-htmlmediaelement-sinkid")]
    public string SinkId => _sinkId;

    /// <summary>
    /// Changes the audio output device of the element (https://w3c.github.io/mediacapture-output/#dom-htmlmediaelement-setsinkid):
    /// the empty string for the default device, or the identifier of an <c>audiooutput</c> device that
    /// <see cref="MediaDevices.EnumerateDevices"/> lists (the outputs of the microphones the document captured, and the devices
    /// chosen with <see cref="MediaDevices.SelectAudioOutput"/>). <see cref="SinkId"/> changes in a task, when the task
    /// completes. The task fails with a <c>NotAllowedError</c> <see cref="DomException"/> when the document is not allowed to
    /// use the <c>speaker-selection</c> feature, and with a <c>NotFoundError</c> for another device. A prerendering document
    /// changes the device once it is activated.
    /// </summary>
    /// <param name="sinkId">The identifier of the device.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-output/#dom-htmlmediaelement-setsinkid"/>
    [IdlMember("setSinkId", "https://w3c.github.io/mediacapture-output/#dom-htmlmediaelement-setsinkid")]
    public Task SetSinkId(string sinkId)
    {
        ArgumentNullException.ThrowIfNull(sinkId);

        // A prerendering document changes the device once it is activated, as in Chromium (the Prerendering specification
        // delays selectAudioOutput(): https://wicg.github.io/nav-speculation/prerendering.html#audio-output-patch)
        var window = GlobalScope.Current as Window ?? NodeDocument.DefaultView;
        return Document.DelayWhilePrerendering(window, () => SetSinkIdCore(window, sinkId));
    }

    private Task SetSinkIdCore(Window? window, string sinkId)
    {
        // Steps 1-3: the document of the current global object (the global object of the element without script)
        var document = window?.Document ?? NodeDocument;
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.SpeakerSelection))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to use the speaker-selection feature"));

        // Step 4 (the transient activation that the user agent may require) is not required, as in the browsers
        if (string.Equals(sinkId, _sinkId, StringComparison.Ordinal))
            return Task.CompletedTask;

        if (window is null)
        {
            if (sinkId.Length > 0)
                return Task.FromException(DomException.NotFound($"The audio output device '{sinkId}' does not exist"));

            _sinkId = "";
            return Task.CompletedTask;
        }

        // In parallel: the device must be one that enumerateDevices() lists; a task then sets the sink identifier
        var result = new TaskCompletionSource();
        window.QueueGlobalTask(() =>
        {
            if (sinkId.Length > 0 && !window.Navigator.MediaDevices.IsExposedAudioOutput(sinkId))
            {
                result.TrySetException(DomException.NotFound($"The audio output device '{sinkId}' does not exist"));
                return;
            }

            _sinkId = sinkId;
            result.TrySetResult();
        });
        return result.Task;
    }
}
