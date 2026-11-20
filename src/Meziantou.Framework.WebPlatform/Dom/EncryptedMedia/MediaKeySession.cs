using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A key session of a <see cref="MediaKeys"/> object (https://w3c.github.io/encrypted-media/#mediakeysession-interface):
/// the application generates a license request from Initialization Data (<see cref="GenerateRequest"/>), sends the
/// <c>message</c> event to the license server, and gives the license to <see cref="Update"/>. With Clear Key, the license
/// is a JSON Web Key Set that contains the keys.
/// </summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession"/>
[IdlInterface("MediaKeySession", "https://w3c.github.io/encrypted-media/#dom-mediakeysession")]
public sealed class MediaKeySession : EventTarget
{
    /// <summary>The longest session ID that <see cref="Load"/> accepts (the Clear Key session IDs are 32-bit integers).</summary>
    private const int MaxSessionIdLength = 64;

    // The persistent sessions that are not closed, in any document: a persistent session can only be loaded once
    private static readonly ConditionalWeakTable<MediaKeysStorageBackend, HashSet<(string Origin, string SessionId)>> LoadedSessions = [];

    // The sessions that are not closed, by window
    private static readonly ConditionalWeakTable<Window, List<MediaKeySession>> OpenSessions = [];

    private readonly MediaKeys _mediaKeys;
    private readonly MediaKeySessionType _sessionType;
    private readonly TaskCompletionSource<MediaKeySessionClosedReason> _closed = new();
    private bool _uninitialized = true;
    private bool _callable;
    private bool _closingOrClosed;

    // The state of the Clear Key session: the keys of its license, and the key IDs of its record of license destruction
    private List<ClearKeyKey> _keys = [];
    private List<byte[]>? _releasedKeyIds;
    private (MediaKeysStorageBackend Backend, string Origin)? _loadedFrom;

    internal MediaKeySession(MediaKeys mediaKeys, MediaKeySessionType sessionType)
    {
        _mediaKeys = mediaKeys;
        _sessionType = sessionType;
        PromiseSlots.Register(_closed.Task);
        KeyStatuses = new MediaKeyStatusMap();
        lock (OpenSessions)
        {
            OpenSessions.GetValue(mediaKeys.Window, _ => []).Add(this);
        }
    }

    internal override GlobalScope? RelevantGlobal => _mediaKeys.Window;

    private Window Window => _mediaKeys.Window;

    /// <summary>Gets the session ID, or the empty string until the license request is generated or the session is loaded.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-sessionid"/>
    [IdlMember("sessionId", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-sessionid")]
    public string SessionId { get; private set; } = "";

    /// <summary>Gets the expiration time of the keys, in milliseconds since the epoch, or NaN (always NaN with Clear Key).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-expiration"/>
    [IdlMember("expiration", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-expiration")]
    public double Expiration { get; private set; } = double.NaN;

    /// <summary>Gets the task that completes with the reason when the session is closed (it never fails).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-closed"/>
    [IdlMember("closed", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-closed")]
    public Task<MediaKeySessionClosedReason> Closed => _closed.Task;

    /// <summary>Gets the statuses of the keys known to the session.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-keystatuses"/>
    [IdlMember("keyStatuses", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-keystatuses")]
    public MediaKeyStatusMap KeyStatuses { get; }

    /// <summary>Gets or sets the event handler of the <c>keystatuseschange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-onkeystatuseschange"/>
    [IdlMember("onkeystatuseschange", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-onkeystatuseschange")]
    public Action<Event>? OnKeyStatusesChange
    {
        get => GetEventHandler("keystatuseschange");
        set => SetEventHandler("keystatuseschange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-onmessage"/>
    [IdlMember("onmessage", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>
    /// Generates a license request from Initialization Data (https://w3c.github.io/encrypted-media/#dom-mediakeysession-generaterequest):
    /// the request is the <see cref="MediaKeyMessageEvent"/> fired once the task completes. With Clear Key, the request lists
    /// the key IDs of the Initialization Data. The task fails with a <see cref="TypeErrorException"/> when the data is empty
    /// or invalid, a <c>NotSupportedError</c> when the type is not supported or the data has no key ID for Clear Key, and an
    /// <c>InvalidStateError</c> when the session is already initialized or closed.
    /// </summary>
    /// <param name="initDataType">The Initialization Data Type: <c>cenc</c>, <c>keyids</c> or <c>webm</c>.</param>
    /// <param name="initData">The Initialization Data.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-generaterequest"/>
    [IdlMember("generateRequest", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-generaterequest")]
    public Task GenerateRequest(string initDataType, ReadOnlySpan<byte> initData)
    {
        ArgumentNullException.ThrowIfNull(initDataType);
        if (_closingOrClosed)
            return Task.FromException(DomException.InvalidState("The session is closed"));

        if (!_uninitialized)
            return Task.FromException(DomException.InvalidState("The session is already initialized"));

        _uninitialized = false;
        if (initDataType.Length == 0)
            return Task.FromException(new TypeErrorException("The Initialization Data Type is empty"));

        if (initData.IsEmpty)
            return Task.FromException(new TypeErrorException("The Initialization Data is empty"));

        if (!ClearKey.IsSupportedInitDataType(initDataType))
            return Task.FromException(DomException.NotSupported("The Initialization Data Type is not supported"));

        var data = initData.ToArray();
        var promise = new TaskCompletionSource();
        Window.QueueGlobalTask(() =>
        {
            if (ClearKey.ExtractKeyIds(initDataType, data) is not { } keyIds)
            {
                promise.TrySetException(new TypeErrorException("The Initialization Data is not valid"));
                return;
            }

            if (keyIds.Count == 0)
            {
                promise.TrySetException(DomException.NotSupported("The Initialization Data has no key ID for Clear Key"));
                return;
            }

            if (_closingOrClosed)
            {
                promise.TrySetException(DomException.InvalidState("The session is closed"));
                return;
            }

            SessionId = CreateSessionId();
            _callable = true;
            promise.TrySetResult();
            QueueMessage(MediaKeyMessageType.LicenseRequest, ClearKey.CreateLicenseRequest(keyIds, _sessionType));
        });

        return promise.Task;
    }

    /// <summary>
    /// Loads the data stored for a persistent session (https://w3c.github.io/encrypted-media/#dom-mediakeysession-load):
    /// the task completes with false when no data is stored for the session ID. The keys of the license are usable; a session
    /// whose license was removed has its record of license destruction, sent again as a <c>license-release</c> message.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-load"/>
    [IdlMember("load", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-load")]
    public Task<bool> Load(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        if (_closingOrClosed)
            return Task.FromException<bool>(DomException.InvalidState("The session is closed"));

        if (!_uninitialized)
            return Task.FromException<bool>(DomException.InvalidState("The session is already initialized"));

        _uninitialized = false;
        if (sessionId.Length == 0)
            return Task.FromException<bool>(new TypeErrorException("The session ID is empty"));

        if (_sessionType != MediaKeySessionType.PersistentLicense)
            return Task.FromException<bool>(new TypeErrorException("Only a persistent session can be loaded"));

        var promise = new TaskCompletionSource<bool>();
        Window.QueueGlobalTask(() =>
        {
            if (sessionId.Length > MaxSessionIdLength || !sessionId.All(char.IsAsciiLetterOrDigit))
            {
                promise.TrySetException(new TypeErrorException("The session ID is not valid"));
                return;
            }

            if (GetOpenSessions(Window).Exists(session => session != this && !session._closingOrClosed && session.SessionId == sessionId))
            {
                promise.TrySetException(new QuotaExceededError("A session with this session ID is open in the document"));
                return;
            }

            if (_closingOrClosed || GetStorage() is not { } storage || !_mediaKeys.PersistentStateAllowed)
            {
                promise.TrySetResult(false);
                return;
            }

            if (storage.Backend.Load(storage.Origin, _mediaKeys.KeySystem, sessionId) is not { } stored || PersistedSession.Parse(stored) is not { } session)
            {
                promise.TrySetResult(false);
                return;
            }

            lock (LoadedSessions)
            {
                var loaded = LoadedSessions.GetValue(storage.Backend, _ => []);
                if (!loaded.Add((storage.Origin, sessionId)))
                {
                    promise.TrySetException(new QuotaExceededError("The persistent session is already loaded"));
                    return;
                }
            }

            _loadedFrom = storage;
            SessionId = sessionId;
            _callable = true;
            if (session.ReleasedKeyIds is { } released)
            {
                // The record of license destruction is sent again until it is acknowledged
                _releasedKeyIds = released;
                UpdateKeyStatuses([.. released.Select(keyId => (keyId, MediaKeyStatus.Released))]);
                promise.TrySetResult(true);
                QueueMessage(MediaKeyMessageType.LicenseRelease, ClearKey.CreateLicenseRelease(released));
                return;
            }

            SetKeys(session.Keys);
            promise.TrySetResult(true);
        });

        return promise.Task;
    }

    /// <summary>
    /// Provides a message of the license server to the CDM (https://w3c.github.io/encrypted-media/#dom-mediakeysession-update):
    /// with Clear Key, a license (a JSON Web Key Set, https://w3c.github.io/encrypted-media/#clear-key-license-format) whose
    /// keys become usable, or the acknowledgement of a license release, which closes the session. The task fails with a
    /// <see cref="TypeErrorException"/> when the response is empty or invalid, and an <c>InvalidStateError</c> when the
    /// session has no license request or is closed.
    /// </summary>
    /// <param name="response">The response (UTF-8 JSON for Clear Key, at most 64 KiB).</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-update"/>
    [IdlMember("update", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-update")]
    public Task Update(ReadOnlySpan<byte> response)
    {
        if (_closingOrClosed)
            return Task.FromException(DomException.InvalidState("The session is closed"));

        if (!_callable)
            return Task.FromException(DomException.InvalidState("The session has no license request"));

        if (response.IsEmpty)
            return Task.FromException(new TypeErrorException("The response is empty"));

        var copy = response.ToArray();
        var promise = new TaskCompletionSource();
        Window.QueueGlobalTask(() =>
        {
            if (_closingOrClosed)
            {
                promise.TrySetException(DomException.InvalidState("The session is closed"));
                return;
            }

            if (ClearKey.ParseResponse(copy) is not { } parsed)
            {
                promise.TrySetException(new TypeErrorException("The response is not a valid Clear Key license"));
                return;
            }

            if (parsed.Keys is { } keys)
            {
                // A temporary session only accepts a temporary license, and a persistent session a persistent one
                if (parsed.SessionType != _sessionType)
                {
                    promise.TrySetException(new TypeErrorException("The type of the license does not match the session type"));
                    return;
                }

                // The new keys are added to the keys of the session (a key ID given again replaces its key)
                var merged = _keys.Where(existing => !keys.Any(key => key.KeyId.AsSpan().SequenceEqual(existing.KeyId))).Concat(keys).ToList();
                if (merged.Count > ClearKey.MaxKeyCount)
                {
                    promise.TrySetException(new QuotaExceededError("The session has too many keys"));
                    return;
                }

                if (_sessionType == MediaKeySessionType.PersistentLicense)
                {
                    if (GetStorage() is not { } storage)
                    {
                        promise.TrySetException(DomException.InvalidState("The session cannot be stored"));
                        return;
                    }

                    storage.Backend.Save(storage.Origin, _mediaKeys.KeySystem, SessionId, PersistedSession.Serialize(merged, releasedKeyIds: null));
                    _releasedKeyIds = null;

                    // The stored session is open: another document cannot load it until it is closed
                    if (_loadedFrom is null)
                    {
                        lock (LoadedSessions)
                        {
                            LoadedSessions.GetValue(storage.Backend, _ => []).Add((storage.Origin, SessionId));
                        }

                        _loadedFrom = storage;
                    }
                }

                SetKeys(merged);
                promise.TrySetResult();
                return;
            }

            // The acknowledgement of a record of license destruction closes a persistent session and clears its data
            if (_sessionType == MediaKeySessionType.PersistentLicense && _releasedKeyIds is not null)
            {
                if (GetStorage() is { } storage)
                {
                    storage.Backend.Delete(storage.Origin, _mediaKeys.KeySystem, SessionId);
                }

                RunSessionClosed(MediaKeySessionClosedReason.ReleaseAcknowledged);
                promise.TrySetResult();
                return;
            }

            promise.TrySetException(new TypeErrorException("The response is not a license"));
        });

        return promise.Task;
    }

    /// <summary>
    /// Closes the session (https://w3c.github.io/encrypted-media/#dom-mediakeysession-close): its keys are no longer usable,
    /// and <see cref="Closed"/> completes with <see cref="MediaKeySessionClosedReason.ClosedByApplication"/>. The data of a
    /// persistent session stays stored.
    /// </summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-close"/>
    [IdlMember("close", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-close")]
    public Task Close()
    {
        if (_closingOrClosed)
            return Task.CompletedTask;

        if (!_callable)
            return Task.FromException(DomException.InvalidState("The session has no license request"));

        _closingOrClosed = true;
        var promise = new TaskCompletionSource();
        Window.QueueGlobalTask(() =>
        {
            promise.TrySetResult();
            RunSessionClosed(MediaKeySessionClosedReason.ClosedByApplication);
        });

        return promise.Task;
    }

    /// <summary>
    /// Removes the license and the keys of the session (https://w3c.github.io/encrypted-media/#dom-mediakeysession-remove):
    /// the key statuses are cleared. A persistent session stores a record of license destruction, sent as a
    /// <c>license-release</c> message until its acknowledgement is given to <see cref="Update"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysession-remove"/>
    [IdlMember("remove", "https://w3c.github.io/encrypted-media/#dom-mediakeysession-remove")]
    public Task Remove()
    {
        if (_closingOrClosed)
            return Task.FromException(DomException.InvalidState("The session is closed"));

        if (!_callable)
            return Task.FromException(DomException.InvalidState("The session has no license request"));

        var promise = new TaskCompletionSource();
        Window.QueueGlobalTask(() =>
        {
            if (_closingOrClosed)
            {
                promise.TrySetException(DomException.InvalidState("The session is closed"));
                return;
            }

            byte[]? message = null;
            if (_sessionType == MediaKeySessionType.PersistentLicense)
            {
                var released = _releasedKeyIds ?? [.. _keys.Select(key => key.KeyId)];
                if (GetStorage() is { } storage)
                {
                    storage.Backend.Save(storage.Origin, _mediaKeys.KeySystem, SessionId, PersistedSession.Serialize(keys: null, released));
                }

                _releasedKeyIds = released;
                message = ClearKey.CreateLicenseRelease(released);
            }

            // Clear Key empties the key statuses (https://w3c.github.io/encrypted-media/#clear-key-behavior)
            _keys = [];
            _mediaKeys.DecryptionKeys.SetSessionKeys(this, []);
            UpdateKeyStatuses([]);
            Expiration = double.NaN;
            promise.TrySetResult();
            if (message is not null)
            {
                QueueMessage(MediaKeyMessageType.LicenseRelease, message);
            }
        });

        return promise.Task;
    }

    /// <summary>The keys of the session are usable: the key statuses are updated, and the media elements may resume their playback.</summary>
    private void SetKeys(IReadOnlyList<ClearKeyKey> keys)
    {
        _keys = [.. keys];
        _mediaKeys.DecryptionKeys.SetSessionKeys(this, _keys);
        UpdateKeyStatuses([.. _keys.Select(key => (key.KeyId, MediaKeyStatus.Usable))]);
    }

    /// <summary>https://w3c.github.io/encrypted-media/#update-key-statuses</summary>
    private void UpdateKeyStatuses(IReadOnlyList<(byte[] KeyId, MediaKeyStatus Status)> statuses)
    {
        KeyStatuses.Replace(statuses);
        Window.QueueGlobalTask(() => FireEvent(new Event("keystatuseschange")));
        _mediaKeys.OnKeysChanged();
    }

    /// <summary>https://w3c.github.io/encrypted-media/#queue-message</summary>
    private void QueueMessage(MediaKeyMessageType messageType, byte[] message)
    {
        Window.QueueGlobalTask(() =>
        {
            if (!_closed.Task.IsCompleted)
            {
                FireEvent(new MediaKeyMessageEvent("message", new MediaKeyMessageEventInit { MessageType = messageType, Message = message }));
            }
        });
    }

    /// <summary>https://w3c.github.io/encrypted-media/#session-closed</summary>
    private void RunSessionClosed(MediaKeySessionClosedReason reason)
    {
        if (_closed.Task.IsCompleted)
            return;

        _closingOrClosed = true;
        _keys = [];
        _mediaKeys.DecryptionKeys.SetSessionKeys(this, []);
        UpdateKeyStatuses([]);
        Expiration = double.NaN;
        if (_loadedFrom is { } loadedFrom)
        {
            lock (LoadedSessions)
            {
                if (LoadedSessions.TryGetValue(loadedFrom.Backend, out var loaded))
                {
                    loaded.Remove((loadedFrom.Origin, SessionId));
                }
            }
        }

        lock (OpenSessions)
        {
            if (OpenSessions.TryGetValue(Window, out var sessions))
            {
                sessions.Remove(this);
            }
        }

        _closed.TrySetResult(reason);
    }

    private static List<MediaKeySession> GetOpenSessions(Window window)
    {
        lock (OpenSessions)
        {
            return OpenSessions.TryGetValue(window, out var sessions) ? [.. sessions] : [];
        }
    }

    /// <summary>The storage of the persistent sessions of the document, partitioned by its storage key, or null (an opaque origin).</summary>
    private (MediaKeysStorageBackend Backend, string Origin)? GetStorage()
        => StorageEndpoints.Resolve(Window, Window.Options.MediaKeysStorage, context => context.MediaKeysStorageBackend, () => new InMemoryMediaKeysStorageBackend());

    /// <summary>
    /// A session ID: a numerical value representable by a 32-bit integer (https://w3c.github.io/encrypted-media/#clear-key-behavior),
    /// unique among the open sessions of the document, and among the stored sessions of the origin for a persistent session.
    /// </summary>
    private string CreateSessionId()
    {
        var storage = _sessionType == MediaKeySessionType.PersistentLicense ? GetStorage() : null;
        var open = GetOpenSessions(Window);
        while (true)
        {
            var id = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (open.Exists(session => session.SessionId == id))
                continue;

            if (storage is { } s && s.Backend.Exists(s.Origin, _mediaKeys.KeySystem, id))
                continue;

            return id;
        }
    }

    /// <summary>The data of a persistent Clear Key session in the <see cref="MediaKeysStorageBackend"/>: its keys, or its record of license destruction.</summary>
    private sealed record PersistedSession(List<ClearKeyKey> Keys, List<byte[]>? ReleasedKeyIds)
    {
        public static byte[] Serialize(IReadOnlyList<ClearKeyKey>? keys, IReadOnlyList<byte[]>? releasedKeyIds)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "persistent-license");
                if (keys is not null)
                {
                    writer.WriteStartArray("keys");
                    foreach (var key in keys)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("kty", "oct");
                        writer.WriteString("kid", Base64Url.EncodeToString(key.KeyId));
                        writer.WriteString("k", Base64Url.EncodeToString(key.Key));
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                if (releasedKeyIds is not null)
                {
                    writer.WriteStartArray("kids");
                    foreach (var keyId in releasedKeyIds)
                    {
                        writer.WriteStringValue(Base64Url.EncodeToString(keyId));
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }

        /// <summary>Parses the stored data with the parser of the responses (the backend is not trusted), or returns null.</summary>
        public static PersistedSession? Parse(byte[] data) => ClearKey.ParseResponse(data) switch
        {
            { Keys: { } keys, SessionType: MediaKeySessionType.PersistentLicense } => new PersistedSession([.. keys], ReleasedKeyIds: null),
            { ReleasedKeyIds: { } released } => new PersistedSession([], [.. released]),
            _ => null,
        };
    }
}

/// <summary>
/// The statuses of the keys known to a key session (https://w3c.github.io/encrypted-media/#mediakeystatusmap-interface): a
/// read-only map of key IDs, sorted by key ID, replaced as a whole when the keys of the session change.
/// </summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap"/>
[IdlInterface("MediaKeyStatusMap", "https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap")]
public sealed class MediaKeyStatusMap : IEnumerable<KeyValuePair<byte[], MediaKeyStatus>>
{
    private (byte[] KeyId, MediaKeyStatus Status)[] _entries = [];

    internal MediaKeyStatusMap()
    {
    }

    /// <summary>Gets the number of known keys.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap-size"/>
    [IdlMember("size", "https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap-size")]
    public uint Size => (uint)_entries.Length;

    /// <summary>Returns whether the status of a key is known.</summary>
    /// <param name="keyId">The key ID.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap-has"/>
    [IdlMember("has", "https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap-has")]
    public bool Has(ReadOnlySpan<byte> keyId) => Get(keyId) is not null;

    /// <summary>Returns the status of a key, or null when it is not known.</summary>
    /// <param name="keyId">The key ID.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap-get"/>
    [IdlMember("get", "https://w3c.github.io/encrypted-media/#dom-mediakeystatusmap-get")]
    public MediaKeyStatus? Get(ReadOnlySpan<byte> keyId)
    {
        foreach (var (id, status) in _entries)
        {
            if (keyId.SequenceEqual(id))
                return status;
        }

        return null;
    }

    /// <summary>Replaces the entries: the snapshot of the pairs is sorted by key ID.</summary>
    internal void Replace(IReadOnlyList<(byte[] KeyId, MediaKeyStatus Status)> statuses)
    {
        var entries = statuses.Select(entry => (entry.KeyId, entry.Status)).ToArray();
        Array.Sort(entries, (x, y) => ClearKey.CompareKeyIds(x.KeyId, y.KeyId));
        _entries = entries;
    }

    /// <summary>Returns the pairs of key ID and status, sorted by key ID. Each key ID is a new array.</summary>
    [NotInIdl]
    public IEnumerator<KeyValuePair<byte[], MediaKeyStatus>> GetEnumerator()
    {
        foreach (var (keyId, status) in _entries)
        {
            yield return new KeyValuePair<byte[], MediaKeyStatus>([.. keyId], status);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
