namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Specifies the type of a CODEOWNERS entry.
/// </summary>
public enum CodeOwnersEntryType
{
    /// <summary>
    /// The entry represents a username.
    /// </summary>
    Username,

    /// <summary>
    /// The entry represents an email address.
    /// </summary>
    EmailAddress,

    /// <summary>
    /// The entry has no owner specified.
    /// </summary>
    None,
}
