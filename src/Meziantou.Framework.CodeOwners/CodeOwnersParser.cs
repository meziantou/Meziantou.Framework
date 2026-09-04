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

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    [Obsolete($"Use {nameof(CodeOwnersFile)}.{nameof(CodeOwnersFile.TryParse)} instead.")]
    public static bool TryParse(string content, [NotNullWhen(true)] out CodeOwnersFile? file) => CodeOwnersFile.TryParse(content, out file);

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the first error found in <paramref name="content"/>; otherwise, <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    [Obsolete($"Use {nameof(CodeOwnersFile)}.{nameof(CodeOwnersFile.TryParse)} instead.")]
    public static bool TryParse(string content, [NotNullWhen(true)] out CodeOwnersFile? file, out CodeOwnersParseError error) => CodeOwnersFile.TryParse(content, out file, out error);
}
