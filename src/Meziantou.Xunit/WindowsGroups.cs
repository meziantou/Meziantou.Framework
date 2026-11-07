namespace Meziantou.Xunit;

/// <summary>
/// Describes the Windows group membership a test requires. Any value other than <see cref="Any"/> also requires Windows.
/// </summary>
public enum WindowsGroups
{
    /// <summary>
    /// The Windows group membership is not part of the condition.
    /// </summary>
    Any = 0,

    /// <summary>
    /// The current user is a member of the built-in <c>Users</c> group and is not running elevated as an administrator.
    /// </summary>
    User = 1,

    /// <summary>
    /// The current user is running elevated as a member of the built-in <c>Administrators</c> group.
    /// </summary>
    Administrator = 2,
}
