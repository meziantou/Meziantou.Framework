namespace Meziantou.Framework.CodeOwners;

/// <summary>Specifies how an owner of a CODEOWNERS entry is identified.</summary>
public enum CodeOwnerType
{
    /// <summary>The owner is a username or a team (e.g., <c>@user</c> or <c>@org/team</c>).</summary>
    Username,

    /// <summary>The owner is an email address (e.g., <c>user@example.com</c>).</summary>
    EmailAddress,
}
