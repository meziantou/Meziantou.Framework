namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storage of the persistent key sessions of Encrypted Media Extensions (https://w3c.github.io/encrypted-media/#session-storage):
/// the data of the <c>persistent-license</c> sessions (their keys, or their record of license destruction), stored by
/// <c>update()</c> and <c>remove()</c> and read by <c>load()</c> (see <see cref="BrowsingContextOptions.MediaKeysStorage"/>).
/// </summary>
/// <remarks>
/// <para>
/// The data is partitioned by storage key: the <c>origin</c> parameter is the serialization of the storage key of the
/// document (its origin, and the top-level site of a third-party context), so that a session is only visible to the
/// documents of its origin in the same partition. The data contains key material: a persistent backend should protect it as
/// the browsers protect their licenses.
/// </para>
/// <para>The methods can be called from the threads of several browsing contexts: an implementation must be thread-safe.</para>
/// </remarks>
public abstract class MediaKeysStorageBackend
{
    /// <summary>Returns the data of a session, or null when no data is stored for the session.</summary>
    /// <param name="origin">The serialization of the storage key of the document.</param>
    /// <param name="keySystem">The key system (e.g. <c>org.w3.clearkey</c>).</param>
    /// <param name="sessionId">The session ID.</param>
    public abstract byte[]? Load(string origin, string keySystem, string sessionId);

    /// <summary>Stores the data of a session, replacing the previous data.</summary>
    /// <param name="origin">The serialization of the storage key of the document.</param>
    /// <param name="keySystem">The key system.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="data">The data of the session.</param>
    public abstract void Save(string origin, string keySystem, string sessionId, ReadOnlySpan<byte> data);

    /// <summary>Removes the data of a session.</summary>
    /// <param name="origin">The serialization of the storage key of the document.</param>
    /// <param name="keySystem">The key system.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>True when data was removed.</returns>
    public abstract bool Delete(string origin, string keySystem, string sessionId);

    /// <summary>Returns whether data is stored for a session: the session IDs of the persistent sessions are unique within an origin over time.</summary>
    /// <param name="origin">The serialization of the storage key of the document.</param>
    /// <param name="keySystem">The key system.</param>
    /// <param name="sessionId">The session ID.</param>
    public virtual bool Exists(string origin, string keySystem, string sessionId) => Load(origin, keySystem, sessionId) is not null;
}

/// <summary>A <see cref="MediaKeysStorageBackend"/> that keeps the sessions in memory.</summary>
public sealed class InMemoryMediaKeysStorageBackend : MediaKeysStorageBackend
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Origin, string KeySystem, string SessionId), byte[]> _sessions = [];

    /// <inheritdoc/>
    public override byte[]? Load(string origin, string keySystem, string sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue((origin, keySystem, sessionId), out var data) ? [.. data] : null;
        }
    }

    /// <inheritdoc/>
    public override void Save(string origin, string keySystem, string sessionId, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(keySystem);
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_lock)
        {
            _sessions[(origin, keySystem, sessionId)] = data.ToArray();
        }
    }

    /// <inheritdoc/>
    public override bool Delete(string origin, string keySystem, string sessionId)
    {
        lock (_lock)
        {
            return _sessions.Remove((origin, keySystem, sessionId));
        }
    }

    /// <inheritdoc/>
    public override bool Exists(string origin, string keySystem, string sessionId)
    {
        lock (_lock)
        {
            return _sessions.ContainsKey((origin, keySystem, sessionId));
        }
    }
}
