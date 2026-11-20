namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A CDM instance and the keys of its sessions (https://w3c.github.io/encrypted-media/#mediakeys-interface): the media
/// elements whose <see cref="HtmlMediaElement.MediaKeys"/> is the object decrypt their media data with the usable keys of
/// its sessions. Clear Key allows a <see cref="MediaKeys"/> object to be used by several media elements.
/// </summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeys"/>
[IdlInterface("MediaKeys", "https://w3c.github.io/encrypted-media/#dom-mediakeys")]
public sealed class MediaKeys
{
    private readonly List<MediaKeySessionType> _sessionTypes;
    private readonly List<HtmlMediaElement> _elements = [];

    internal MediaKeys(Window window, string keySystem, bool persistentStateAllowed, List<MediaKeySessionType> sessionTypes)
    {
        Window = window;
        KeySystem = keySystem;
        PersistentStateAllowed = persistentStateAllowed;
        _sessionTypes = sessionTypes;
    }

    internal Window Window { get; }

    internal string KeySystem { get; }

    /// <summary>Whether the CDM instance can persist state (the <c>persistentState</c> of the configuration is <c>required</c>).</summary>
    internal bool PersistentStateAllowed { get; }

    /// <summary>The usable keys of the sessions created by the object, read by the media pipeline (from any thread).</summary>
    internal MediaKeysDecryptionKeys DecryptionKeys { get; } = new();

    /// <summary>The media elements whose <c>mediaKeys</c> attribute is the object.</summary>
    internal IReadOnlyList<HtmlMediaElement> Elements => _elements;

    /// <summary>
    /// Creates a key session (https://w3c.github.io/encrypted-media/#dom-mediakeys-createsession). It throws a
    /// <c>NotSupportedError</c> when the configuration does not support the session type (a <c>persistent-license</c>
    /// session needs a configuration whose <c>persistentState</c> is <c>required</c>).
    /// </summary>
    /// <param name="sessionType">The type of the session.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeys-createsession"/>
    [IdlMember("createSession", "https://w3c.github.io/encrypted-media/#dom-mediakeys-createsession")]
    public MediaKeySession CreateSession(MediaKeySessionType sessionType = MediaKeySessionType.Temporary)
    {
        if (!_sessionTypes.Contains(sessionType))
            throw DomException.NotSupported("The configuration does not support the session type");

        return new MediaKeySession(this, sessionType);
    }

    /// <summary>
    /// Returns the status of the keys for a policy (https://w3c.github.io/encrypted-media/#dom-mediakeys-getstatusforpolicy):
    /// Clear Key resolves with <c>usable</c>. The task fails with a <see cref="TypeErrorException"/> when the policy has no
    /// member.
    /// </summary>
    /// <param name="policy">The policy requirements.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeys-getstatusforpolicy"/>
    [IdlMember("getStatusForPolicy", "https://w3c.github.io/encrypted-media/#dom-mediakeys-getstatusforpolicy")]
    public Task<MediaKeyStatus> GetStatusForPolicy(MediaKeysPolicy? policy = null)
    {
        if (policy?.MinHdcpVersion is null)
            return Task.FromException<MediaKeyStatus>(new TypeErrorException("The policy has no member"));

        var promise = new TaskCompletionSource<MediaKeyStatus>();
        Window.QueueGlobalTask(() => promise.TrySetResult(MediaKeyStatus.Usable));
        return promise.Task;
    }

    /// <summary>
    /// Provides the server certificate of the license server (https://w3c.github.io/encrypted-media/#dom-mediakeys-setservercertificate):
    /// Clear Key does not use server certificates, so the task completes with false. It fails with a
    /// <see cref="TypeErrorException"/> when the certificate is empty.
    /// </summary>
    /// <param name="serverCertificate">The certificate.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeys-setservercertificate"/>
    [IdlMember("setServerCertificate", "https://w3c.github.io/encrypted-media/#dom-mediakeys-setservercertificate")]
    public Task<bool> SetServerCertificate(ReadOnlySpan<byte> serverCertificate)
    {
        if (serverCertificate.IsEmpty)
            return Task.FromException<bool>(new TypeErrorException("The server certificate is empty"));

        return Task.FromResult(false);
    }

    internal void AddElement(HtmlMediaElement element)
    {
        if (!_elements.Contains(element))
        {
            _elements.Add(element);
        }
    }

    internal void RemoveElement(HtmlMediaElement element) => _elements.Remove(element);

    /// <summary>
    /// The keys of a session changed (the end of the Update Key Statuses algorithm, https://w3c.github.io/encrypted-media/#update-key-statuses):
    /// a task runs the Attempt to Resume Playback If Necessary algorithm on each media element that uses the object.
    /// </summary>
    internal void OnKeysChanged()
    {
        foreach (var element in _elements.ToArray())
        {
            Window.QueueGlobalTask(() =>
            {
                if (ReferenceEquals(element.MediaKeys, this))
                {
                    element.AttemptToResumePlaybackIfNecessary();
                }
            });
        }
    }
}

/// <summary>
/// The usable keys of the sessions of a <see cref="MediaKeys"/> object (the available keys of the Attempt to Decrypt
/// algorithm, https://w3c.github.io/encrypted-media/#attempt-to-decrypt). The sessions update it on the thread of their
/// window, and the media pipeline reads it from the thread of the media source (a window or a dedicated worker). The keys
/// never leave the library: they decrypt the media data of the elements that use the object.
/// </summary>
internal sealed class MediaKeysDecryptionKeys
{
    private readonly Lock _lock = new();
    private readonly List<(object Session, ClearKeyKey Key)> _keys = [];
    private readonly List<Action> _listeners = [];

    /// <summary>A number that changes each time the keys change.</summary>
    public int Version { get; private set; }

    /// <summary>Returns the key of a key ID, from any session that has it usable, or null.</summary>
    public byte[]? FindKey(ReadOnlySpan<byte> keyId)
    {
        lock (_lock)
        {
            foreach (var (_, key) in _keys)
            {
                if (key.KeyId.AsSpan().SequenceEqual(keyId))
                    return key.Key;
            }

            return null;
        }
    }

    /// <summary>Replaces the usable keys of a session (an empty list when the session is closed, or its keys are released).</summary>
    public void SetSessionKeys(object session, IReadOnlyList<ClearKeyKey> keys)
    {
        Action[] listeners;
        lock (_lock)
        {
            _keys.RemoveAll(entry => ReferenceEquals(entry.Session, session));
            foreach (var key in keys)
            {
                _keys.Add((session, key));
            }

            Version++;
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
        {
            listener();
        }
    }

    /// <summary>Registers a callback called (on the thread of the window) when the keys change, e.g. by a media source that waits for a key.</summary>
    public void AddListener(Action listener)
    {
        lock (_lock)
        {
            _listeners.Add(listener);
        }
    }

    public void RemoveListener(Action listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }
}
