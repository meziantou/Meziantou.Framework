namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The permissions that the user gave to the origins (https://w3c.github.io/permissions/#permission-store): the states read
/// by <c>navigator.permissions.query()</c> (<see cref="Permissions.Query"/>). It plays the role of the permission prompts of
/// a browser. The state of a permission that was not set is <see cref="PermissionState.Prompt"/>. A store can be shared by
/// several browsing contexts (<see cref="BrowsingContextOptions.Permissions"/>), and used from any thread.
/// </summary>
/// <remarks>
/// The state is keyed by the top-level origin of the document that queries it (the permission key of the specification).
/// The documents that are not allowed to use a feature by their permissions policy, and the documents and workers that are
/// not secure contexts, see the permissions as <see cref="PermissionState.Denied"/>. Changing a state fires the
/// <c>change</c> event of the <see cref="PermissionStatus"/> objects whose state changed.
/// </remarks>
public sealed class PermissionStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Name, string? Origin), PermissionState> _states = [];
    private readonly List<WeakReference<PermissionStatus>> _statuses = [];

    /// <summary>Returns the state of a permission for a top-level origin.</summary>
    /// <param name="name">The name of the powerful feature (e.g. <c>geolocation</c>).</param>
    /// <param name="origin">The serialization of the top-level origin (e.g. <c>https://example.com</c>).</param>
    public PermissionState GetState(string name, string origin)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            if (_states.TryGetValue((name, origin), out var state) || _states.TryGetValue((name, null), out state))
                return state;
        }

        return PermissionState.Prompt;
    }

    /// <summary>Sets the state of a permission for every origin (and removes the states set for an origin).</summary>
    /// <param name="name">The name of the powerful feature (e.g. <c>geolocation</c>).</param>
    /// <param name="state">The new state.</param>
    public void SetState(string name, PermissionState state)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
        {
            foreach (var key in _states.Keys.Where(key => key.Name == name && key.Origin is not null).ToArray())
            {
                _states.Remove(key);
            }

            _states[(name, null)] = state;
        }

        NotifyChanged(name);
    }

    /// <summary>Sets the state of a permission for a top-level origin.</summary>
    /// <param name="name">The name of the powerful feature (e.g. <c>geolocation</c>).</param>
    /// <param name="origin">The serialization of the top-level origin (e.g. <c>https://example.com</c>).</param>
    /// <param name="state">The new state.</param>
    public void SetState(string name, string origin, PermissionState state)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            _states[(name, origin)] = state;
        }

        NotifyChanged(name);
    }

    /// <summary>Registers a status, whose state is read again when the state of its permission changes.</summary>
    internal void Register(PermissionStatus status)
    {
        lock (_lock)
        {
            _statuses.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _statuses.Add(new WeakReference<PermissionStatus>(status));
        }
    }

    /// <summary>https://w3c.github.io/permissions/#dfn-permissionstatus-update-steps for the statuses of a permission.</summary>
    private void NotifyChanged(string name)
    {
        List<PermissionStatus> statuses = [];
        lock (_lock)
        {
            foreach (var reference in _statuses)
            {
                if (reference.TryGetTarget(out var status) && status.Name == name)
                {
                    statuses.Add(status);
                }
            }
        }

        foreach (var status in statuses)
        {
            status.OnPermissionChanged();
        }
    }
}
