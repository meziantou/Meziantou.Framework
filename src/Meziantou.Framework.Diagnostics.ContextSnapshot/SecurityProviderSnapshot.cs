namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

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
