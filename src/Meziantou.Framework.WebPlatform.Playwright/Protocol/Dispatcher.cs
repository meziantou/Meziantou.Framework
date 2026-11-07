using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Playwright.Protocol;

/// <summary>
/// An object of the protocol (a <c>Dispatcher</c> of the Playwright server): it has a type (an interface of the protocol, e.g.
/// <c>Page</c>), a guid, an initializer sent to the client when the object is created (<c>__create__</c>), a parent (the scope
/// whose disposal disposes the object), and it handles the calls of the client (its methods) and sends events.
/// </summary>
internal abstract class Dispatcher
{
    private readonly Dictionary<string, Dispatcher> _children = new(StringComparer.Ordinal);

    /// <summary>Creates the root object (it has no parent and is not created on the client).</summary>
    protected Dispatcher(PlaywrightConnection connection, string type, string guid)
    {
        Connection = connection;
        Type = type;
        Guid = guid;
        connection.Register(this);
    }

    /// <summary>Creates an object, and sends its initializer to the client.</summary>
    protected Dispatcher(Dispatcher parent, string type, string guidPrefix, JsonObject? initializer)
    {
        Connection = parent.Connection;
        Type = type;
        Guid = Connection.CreateGuid(guidPrefix);
        lock (Connection.SyncRoot)
        {
            Parent = parent;
            Connection.Register(this);
            parent._children.Add(Guid, this);
            Connection.SendCreate(parent, type, Guid, initializer ?? []);
        }
    }

    public PlaywrightConnection Connection { get; }

    public string Type { get; }

    public string Guid { get; }

    public Dispatcher? Parent { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Returns a reference to the object, as the parameters and the results reference the objects (<c>{ "guid": ... }</c>).</summary>
    public JsonObject ToReference() => new() { ["guid"] = Guid };

    /// <summary>Handles a call of the client. Returns the result object, or null for a call without result.</summary>
    public virtual ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
        => throw new PlaywrightError($"Mismatching dispatcher: \"{Type}\" does not implement \"{method}\"");

    /// <summary>Sends an event of the object to the client (ignored once the object is disposed).</summary>
    public void SendEvent(string method, JsonObject? parameters = null)
    {
        lock (Connection.SyncRoot)
        {
            if (IsDisposed)
                return;

            Connection.SendEvent(this, method, parameters ?? []);
        }
    }

    /// <summary>Makes this object the parent of <paramref name="child"/> (<c>__adopt__</c>).</summary>
    public void Adopt(Dispatcher child)
    {
        lock (Connection.SyncRoot)
        {
            if (ReferenceEquals(child.Parent, this))
                return;

            child.Parent?._children.Remove(child.Guid);
            _children.Add(child.Guid, child);
            child.Parent = this;
            Connection.SendAdopt(this, child);
        }
    }

    /// <summary>Disposes the object and its children (<c>__dispose__</c>).</summary>
    public void Dispose(string? reason = null)
    {
        lock (Connection.SyncRoot)
        {
            if (IsDisposed)
                return;

            DisposeRecursively();
            Connection.SendDispose(this, reason);
        }
    }

    /// <summary>Disposes the object and its children without notifying the client (the connection is closed).</summary>
    public void DisposeSilently()
    {
        lock (Connection.SyncRoot)
        {
            if (!IsDisposed)
            {
                DisposeRecursively();
            }
        }
    }

    private void DisposeRecursively()
    {
        IsDisposed = true;
        Parent?._children.Remove(Guid);
        Connection.Unregister(this);
        foreach (var child in _children.Values.ToArray())
        {
            child.DisposeRecursively();
        }

        _children.Clear();
        try
        {
            OnDisposed();
        }
#pragma warning disable CA1031 // The cleanup of an object must not prevent the disposal of the others
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Releases the resources of the object, once it is disposed (called while the connection is locked).</summary>
    protected virtual void OnDisposed()
    {
    }
}
