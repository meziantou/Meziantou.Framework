namespace Meziantou.Framework.CodeOwners;

/// <summary>Specifies the type of code owner entry.</summary>
public enum CodeOwnersEntryType
{
    /// <summary>The entry represents a username (e.g., @user).</summary>
    Username,

    /// <summary>The entry represents an email address (e.g., user@example.com).</summary>
    EmailAddress,

    /// <summary>The entry has no owner assigned.</summary>
    None,
}
