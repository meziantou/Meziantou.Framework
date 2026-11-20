namespace Meziantou.Framework.WebPlatform.Dom;

// The extensions of the media elements for Encrypted Media Extensions (https://w3c.github.io/encrypted-media/#htmlmediaelement-extensions):
// the MediaKeys of the element, the encrypted and waitingforkey events, and the playback that waits for the keys of the
// encrypted frames of an attached media source.
public partial class HtmlMediaElement
{
    /// <summary>
    /// The event types of the event handlers that Encrypted Media Extensions add to the media elements (both content and IDL
    /// attributes): Trusted Types and the Sanitizer API treat their content attributes as the other event handlers.
    /// </summary>
    internal static IReadOnlyList<string> EncryptedMediaEventHandlerTypes { get; } = ["encrypted", "waitingforkey"];

    private MediaKeys? _mediaKeys;
    private bool _attachingMediaKeys;

    /// <summary>The playback blocked waiting for key value (https://w3c.github.io/encrypted-media/#dfn-playback-blocked-waiting-for-key).</summary>
    private bool _playbackBlockedWaitingForKey;

    /// <summary>Gets the <see cref="Dom.MediaKeys"/> that decrypt the media data of the element, or null.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-mediakeys"/>
    [IdlMember("mediaKeys", "https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-mediakeys")]
    public MediaKeys? MediaKeys => _mediaKeys;

    /// <summary>Gets or sets the event handler of the <c>encrypted</c> event.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-onencrypted"/>
    [IdlMember("onencrypted", "https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-onencrypted")]
    public Action<Event>? OnEncrypted
    {
        get => GetEventHandler("encrypted");
        set => SetEventHandler("encrypted", value);
    }

    /// <summary>Gets or sets the event handler of the <c>waitingforkey</c> event.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-onwaitingforkey"/>
    [IdlMember("onwaitingforkey", "https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-onwaitingforkey")]
    public Action<Event>? OnWaitingForKey
    {
        get => GetEventHandler("waitingforkey");
        set => SetEventHandler("waitingforkey", value);
    }

    /// <summary>
    /// Sets the <see cref="Dom.MediaKeys"/> that decrypt the media data of the element (https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-setmediakeys):
    /// the frames that wait for a key are decrypted with the keys of its sessions. A <see cref="Dom.MediaKeys"/> object can
    /// be used by several elements. The task fails with an <c>InvalidStateError</c> while a previous call is in progress.
    /// </summary>
    /// <param name="mediaKeys">The media keys, or null to remove the association.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-setmediakeys"/>
    [IdlMember("setMediaKeys", "https://w3c.github.io/encrypted-media/#dom-htmlmediaelement-setmediakeys")]
    public Task SetMediaKeys(MediaKeys? mediaKeys)
    {
        if (_attachingMediaKeys)
            return Task.FromException(DomException.InvalidState("The media keys of the element are being set"));

        if (ReferenceEquals(mediaKeys, _mediaKeys))
            return Task.CompletedTask;

        _attachingMediaKeys = true;
        var promise = new TaskCompletionSource();
        NodeDocument.QueueElementTask(() =>
        {
            _mediaKeys?.RemoveElement(this);
            _mediaKeys = mediaKeys;
            mediaKeys?.AddElement(this);
            _attachingMediaKeys = false;

            // The attached media source decrypts its frames with the keys of the new object
            if (_mediaSource is { } source && _mediaSourceGlobal is { } global)
            {
                source.SetDecryptionKeys(global, mediaKeys?.DecryptionKeys);
            }

            promise.TrySetResult();
            if (mediaKeys is not null)
            {
                AttemptToResumePlaybackIfNecessary();
            }
        });

        return promise.Task;
    }

    /// <summary>
    /// The keys of a session of the <see cref="Dom.MediaKeys"/> of the element changed: the attached media source decrypts the
    /// frames that wait for them, and the playback resumes when the data of the current playback position is decrypted
    /// (https://w3c.github.io/encrypted-media/#resume-playback).
    /// </summary>
    internal void AttemptToResumePlaybackIfNecessary()
    {
        if (_mediaSource is { } source && _mediaSourceGlobal is { } global)
        {
            source.OnDecryptionKeysChanged(global);
        }

        if (_playbackBlockedWaitingForKey)
        {
            UpdateWaitingForKey();
        }
    }

    /// <summary>
    /// The Initialization Data Encountered algorithm (https://w3c.github.io/encrypted-media/#initdata-encountered): an
    /// <c>encrypted</c> event is fired for each Initialization Data. The media data appended to a media source is
    /// CORS-same-origin: the event has the data.
    /// </summary>
    internal void OnInitializationDataEncountered(IReadOnlyList<(string InitDataType, byte[] Data)> initData)
    {
        foreach (var (initDataType, data) in initData)
        {
            var isCorsSameOrigin = IsMediaDataCorsSameOrigin;
            QueueMediaElementTask(() => FireEvent(new MediaEncryptedEvent("encrypted", new MediaEncryptedEventInit
            {
                InitDataType = isCorsSameOrigin ? initDataType : "",
                InitData = isCorsSameOrigin ? data : null,
            })));
        }
    }

    /// <summary>
    /// Runs the Wait for Key algorithm (https://w3c.github.io/encrypted-media/#wait-for-key) when the data of the current
    /// playback position waits for a key, before the ready state changes: the <c>waitingforkey</c> event is queued before
    /// the events of the ready state. The flag is cleared once the data can be decrypted.
    /// </summary>
    private void UpdateWaitingForKey()
    {
        if (_player is not MediaSourcePlayer player || _readyState == HaveNothing || _error is not null)
            return;

        if (player.IsWaitingForKey)
        {
            if (!_playbackBlockedWaitingForKey)
            {
                _playbackBlockedWaitingForKey = true;
                QueueMediaEvent("waitingforkey");
            }
        }
        else
        {
            _playbackBlockedWaitingForKey = false;
        }
    }

    /// <summary>The current playback position changed other than during normal playback (a seek, a new media resource): the element no longer waits for a key.</summary>
    private void ResetWaitingForKey() => _playbackBlockedWaitingForKey = false;
}
