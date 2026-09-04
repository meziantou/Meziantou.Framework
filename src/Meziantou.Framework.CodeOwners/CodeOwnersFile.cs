namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a parsed CODEOWNERS file.
/// <example>
/// <code>
/// var file = CodeOwnersFile.Parse("* @user1\n*.js @js-owner", CodeOwnersDialect.GitHub);
/// // file.Entries[0]: Pattern="*", Owners=[@user1]
/// // file.Entries[1]: Pattern="*.js", Owners=[@js-owner]
/// </code>
/// </example>
/// </summary>
/// <remarks>An instance only exists for a valid file: <see cref="Parse(string, CodeOwnersDialect)"/> throws and <see cref="TryParse(string, CodeOwnersDialect, out CodeOwnersFile)"/> returns <see langword="false"/> rather than returning a partially parsed file.</remarks>
public sealed class CodeOwnersFile
{
    internal CodeOwnersFile(IReadOnlyList<CodeOwnersEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>Gets the entries of the file, in the order they appear in it.</summary>
    /// <remarks>CODEOWNERS resolution is last-match-wins, so the last entry whose <see cref="CodeOwnersEntry.Pattern"/> matches a path owns it.</remarks>
    public IReadOnlyList<CodeOwnersEntry> Entries { get; }

    /// <summary>Parses the content of a CODEOWNERS file.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <returns>The parsed <see cref="CodeOwnersFile"/>.</returns>
    /// <exception cref="CodeOwnersParseException"><paramref name="content"/> is not a valid CODEOWNERS file. Parsing stops at the first error.</exception>
    public static CodeOwnersFile Parse(string content, CodeOwnersDialect dialect)
    {
        return Parse(content.AsSpan(), dialect);
    }

    /// <summary>Parses the content of a CODEOWNERS file.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <returns>The parsed <see cref="CodeOwnersFile"/>.</returns>
    /// <exception cref="CodeOwnersParseException"><paramref name="content"/> is not a valid CODEOWNERS file. Parsing stops at the first error.</exception>
    /// <remarks>The returned <see cref="CodeOwnersFile"/> does not reference <paramref name="content"/>: every value it exposes is copied out of it.</remarks>
    public static CodeOwnersFile Parse(ReadOnlySpan<char> content, CodeOwnersDialect dialect)
    {
        var context = new CodeOwnersParserContext(content, dialect);
        var entries = context.Parse();
        if (context.HasError)
            throw new CodeOwnersParseException(context.CreateError());

        return new CodeOwnersFile(entries);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>Use the <see cref="TryParse(string, CodeOwnersDialect, out CodeOwnersFile, out CodeOwnersParseError)"/> overload to know why the file is invalid.</remarks>
    public static bool TryParse(string content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file)
    {
        return TryParse(content.AsSpan(), dialect, out file, out _);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>Use the <see cref="TryParse(ReadOnlySpan{char}, CodeOwnersDialect, out CodeOwnersFile, out CodeOwnersParseError)"/> overload to know why the file is invalid.</remarks>
    public static bool TryParse(ReadOnlySpan<char> content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file)
    {
        return TryParse(content, dialect, out file, out _);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the first error found in <paramref name="content"/>; otherwise, <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file, out CodeOwnersParseError error)
    {
        return TryParse(content.AsSpan(), dialect, out file, out error);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="dialect">The syntax <paramref name="content"/> uses. Sections are only recognized by <see cref="CodeOwnersDialect.GitLab"/>.</param>
    /// <param name="file">When this method returns <see langword="true"/>, contains the parsed <see cref="CodeOwnersFile"/>; otherwise, <see langword="null"/>.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the first error found in <paramref name="content"/>; otherwise, <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The parsed <see cref="CodeOwnersFile"/> does not reference <paramref name="content"/>: every value it exposes is copied out of it.</remarks>
    public static bool TryParse(ReadOnlySpan<char> content, CodeOwnersDialect dialect, [NotNullWhen(true)] out CodeOwnersFile? file, out CodeOwnersParseError error)
    {
        var context = new CodeOwnersParserContext(content, dialect);
        var entries = context.Parse();
        if (context.HasError)
        {
            file = null;
            error = context.CreateError();
            return false;
        }

        file = new CodeOwnersFile(entries);
        error = default;
        return true;
    }
}
