namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the current user including username, domain name, and interactive status.</summary>
public sealed class UserSnapshot
{
    internal UserSnapshot() { }

    public string UserName { get; } = Environment.UserName;
    public string UserDomainName { get; } = Environment.UserDomainName;
    public bool UserInteractive { get; } = Environment.UserInteractive;
}
