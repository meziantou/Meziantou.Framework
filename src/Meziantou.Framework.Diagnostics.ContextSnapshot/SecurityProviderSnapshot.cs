namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a security provider at a specific point in time.</summary>
public sealed class SecurityProviderSnapshot
{
    internal SecurityProviderSnapshot(string? name, string? path, string? status, string? state, string? stateTimestamp)
    {
        Name = name;
        Path = path;
        Status = status;
        State = state;
        StateTimestamp = stateTimestamp;
    }

    /// <summary>Gets the name of the security provider.</summary>
    public string? Name { get; }
    /// <summary>Gets the path to the security provider executable.</summary>
    public string? Path { get; }
    /// <summary>Gets the status of the security provider.</summary>
    public string? Status { get; }
    /// <summary>Gets the state of the security provider.</summary>
    public string? State { get; }
    /// <summary>Gets the timestamp when the state was last updated.</summary>
    public string? StateTimestamp { get; }
}
