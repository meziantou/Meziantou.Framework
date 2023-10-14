namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class UserSnapshot
{
    internal UserSnapshot() { }

    public string UserName { get; } = Environment.UserName;
    public string UserDomainName { get; } = Environment.UserDomainName;
    public bool UserInteractive { get; } = Environment.UserInteractive;
}
