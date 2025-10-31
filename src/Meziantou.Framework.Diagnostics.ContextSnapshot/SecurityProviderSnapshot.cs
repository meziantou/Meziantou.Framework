namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a security provider (antivirus, firewall, or anti-spyware) including name, path, status, and state.</summary>
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

    public string? Name { get; }
    public string? Path { get; }
    public string? Status { get; }
    public string? State { get; }
    public string? StateTimestamp { get; }
}
