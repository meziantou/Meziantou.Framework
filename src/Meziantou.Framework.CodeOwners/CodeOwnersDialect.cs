namespace Meziantou.Framework.CodeOwners;

/// <summary>Specifies which CODEOWNERS syntax a file uses.</summary>
public enum CodeOwnersDialect
{
    /// <summary>
    /// The syntax supported by GitHub: a pattern followed by its owners, with no sections.
    /// A line starting with <c>[</c> is a pattern, since <c>[</c> opens a character class in gitignore-style patterns.
    /// </summary>
    GitHub,

    /// <summary>
    /// The syntax supported by GitLab, which adds sections on top of the GitHub syntax:
    /// <c>[Name]</c>, <c>^[Name]</c> for an optional section, <c>[Name][2]</c> to declare a reviewer count,
    /// and default owners written after the section name.
    /// </summary>
    GitLab,
}
