namespace Meziantou.Framework.CodeOwners;

/// <summary>Parses CODEOWNERS files used by GitHub and GitLab to define code ownership.</summary>
public static class CodeOwnersParser
{
    /// <summary>Parses the content of a CODEOWNERS file.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <returns>The parsed <see cref="CodeOwnersFile"/>.</returns>
    /// <exception cref="CodeOwnersParseException"><paramref name="content"/> is not a valid CODEOWNERS file. Parsing stops at the first error.</exception>
    [Obsolete($"Use {nameof(CodeOwnersFile)}.{nameof(CodeOwnersFile.Parse)} instead.")]
    public static CodeOwnersFile Parse(string content) => CodeOwnersFile.Parse(content);
}
