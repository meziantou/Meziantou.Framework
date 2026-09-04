namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Represents a parsed CODEOWNERS file.
/// <example>
/// <code>
/// var file = CodeOwnersParser.Parse("* @user1\n*.js @js-owner");
/// // file.Entries[0]: Pattern="*", Owners=[@user1]
/// // file.Entries[1]: Pattern="*.js", Owners=[@js-owner]
/// </code>
/// </example>
/// </summary>
/// <remarks>An instance only exists for a valid file: <see cref="CodeOwnersParser.Parse(string)"/> throws and <see cref="CodeOwnersParser.TryParse(string, out CodeOwnersFile)"/> returns <see langword="false"/> rather than returning a partially parsed file.</remarks>
public sealed class CodeOwnersFile
{
    internal CodeOwnersFile(IReadOnlyList<CodeOwnersEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>Gets the entries of the file, in the order they appear in it.</summary>
    /// <remarks>CODEOWNERS resolution is last-match-wins, so the last entry whose <see cref="CodeOwnersEntry.Pattern"/> matches a path owns it.</remarks>
    public IReadOnlyList<CodeOwnersEntry> Entries { get; }
}
