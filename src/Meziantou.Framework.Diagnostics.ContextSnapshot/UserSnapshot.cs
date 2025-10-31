namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of user information at a specific point in time.</summary>
public sealed class UserSnapshot
{
    internal UserSnapshot() { }

    /// <summary>Gets the user name of the person who is currently logged on.</summary>
    public string UserName { get; } = Environment.UserName;
    /// <summary>Gets the network domain name associated with the current user.</summary>
    public string UserDomainName { get; } = Environment.UserDomainName;
    /// <summary>Gets a value indicating whether the current process is running in user interactive mode.</summary>
    public bool UserInteractive { get; } = Environment.UserInteractive;
}
