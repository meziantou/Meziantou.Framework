namespace Meziantou.Framework.CodeOwners;

/// <summary>Specifies why a CODEOWNERS file is invalid.</summary>
public enum CodeOwnersErrorKind
{
    /// <summary>A section header is missing its closing <c>]</c>.</summary>
    UnterminatedSectionHeader,

    /// <summary>A required reviewer count is missing its closing <c>]</c>.</summary>
    UnterminatedRequiredReviewerCount,

    /// <summary>A required reviewer count is not a positive integer.</summary>
    InvalidRequiredReviewerCount,

    /// <summary>An owner consists of a single <c>@</c>.</summary>
    EmptyMember,

    /// <summary>An owner is neither a username nor an email address.</summary>
    InvalidMember,
}
