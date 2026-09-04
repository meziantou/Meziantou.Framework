# Meziantou.Framework.CodeOwners

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS file](https://docs.github.com/en/github/creating-cloning-and-archiving-repositories/about-code-owners). These files are common on GitHub and GitLab.

Parsing takes the dialect of the file, because the two hosts do not accept the same syntax: sections (`[Name]`, `^[Name]`, `[Name][2]`) are a GitLab extension, and in the GitHub syntax a line starting with `[` is a pattern, since `[` opens a character class.

Each entry is one line of the file: a pattern and the owners it declares.

````c#
CodeOwnersFile file = CodeOwnersFile.Parse("* @user1 docs@example.com", CodeOwnersDialect.GitHub);
// file.Entries[0].Pattern: "*"
// file.Entries[0].Owners[0]: Type=Username, Name="user1"
// file.Entries[0].Owners[1]: Type=EmailAddress, Name="docs@example.com"
````

Entries are returned in file order, and CODEOWNERS resolution is last-match-wins, so the owners of a path are those of the last entry whose pattern matches it:

````c#
CodeOwnersEntry? owningEntry = file.Entries.LastOrDefault(entry => Matches(entry.Pattern, path));
IReadOnlyList<CodeOwner> owners = owningEntry?.Owners ?? [];
// An empty Owners list means the entry explicitly leaves the pattern unowned
````

`Parse` throws a `CodeOwnersParseException` when the file is invalid. The exception reports the first error and where it is:

````c#
try
{
    CodeOwnersFile.Parse("[Section\n* @user1", CodeOwnersDialect.GitLab);
}
catch (CodeOwnersParseException ex)
{
    // ex.Error.Kind: CodeOwnersParseErrorKind.UnterminatedSectionHeader
    // ex.Error.LineNumber: 1
    // ex.Error.LinePosition: 1
    Console.WriteLine(ex.Message);
}
````

Use `TryParse` when an invalid file should not throw. An overload reports the same error without allocating an exception:

````c#
if (CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitHub, out CodeOwnersFile? file, out CodeOwnersParseError error))
{
    // ...
}
else
{
    Console.WriteLine(error); // line 1, position 1: the section header is not terminated by ']'
}
````

A `CodeOwnersFile` only exists for a valid file: neither method hands back a partially parsed one. `Entries`, `Owners` and `DefaultOwners` are read-only views that cannot be cast back to a mutable list.

In the GitLab dialect an owner can also be a role, written `@@developer`, `@@maintainer` or `@@owner` (plural accepted). Anything else after `@@` is an error, as is a `@` inside a username.

````c#
CodeOwnersFile file = CodeOwnersFile.Parse("*.md @@maintainers", CodeOwnersDialect.GitLab);
// file.Entries[0].Owners[0]: Type=Role, Name="maintainers"
````

The `CodeOwnersParser` type is obsolete: its `Parse` method forwards to `CodeOwnersFile` using the GitLab dialect, and will be removed in a future major version.
