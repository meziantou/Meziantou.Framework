namespace Meziantou.Framework.CodeOwners;

/// <summary>Specifies how an owner of a CODEOWNERS entry is identified.</summary>
public enum CodeOwnerType
{
    /// <summary>The owner is a username or a team (e.g., <c>@user</c> or <c>@org/team</c>).</summary>
    Username,

    /// <summary>The owner is an email address (e.g., <c>user@example.com</c>).</summary>
    EmailAddress,

    /// <summary>The owner is a GitLab role (e.g., <c>@@maintainer</c>). Only <c>developer</c>, <c>maintainer</c> and <c>owner</c> are accepted, singular or plural.</summary>
    Role,
}
