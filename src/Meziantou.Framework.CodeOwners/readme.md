# Meziantou.Framework.CodeOwners

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS file](https://docs.github.com/en/github/creating-cloning-and-archiving-repositories/about-code-owners). These files are common on GitHub and GitLab.

Each entry is one line of the file: a pattern and the owners it declares.

````c#
IReadOnlyList<CodeOwnersEntry> entries = CodeOwnersParser.Parse("* @user1 docs@example.com");
// entries[0].Pattern: "*"
// entries[0].Owners[0]: Type=Username, Name="user1"
// entries[0].Owners[1]: Type=EmailAddress, Name="docs@example.com"
````

Entries are returned in file order, and CODEOWNERS resolution is last-match-wins, so the owners of a path are those of the last entry whose pattern matches it:

````c#
CodeOwnersEntry? owningEntry = entries.LastOrDefault(entry => Matches(entry.Pattern, path));
IReadOnlyList<CodeOwner> owners = owningEntry?.Owners ?? [];
// An empty Owners list means the entry explicitly leaves the pattern unowned
````

`Parse` throws a `CodeOwnersParseException` when the file is invalid. The exception reports the first error and where it is:

````c#
try
{
    CodeOwnersParser.Parse("[Section\n* @user1");
}
catch (CodeOwnersParseException ex)
{
    // ex.Kind: CodeOwnersParseErrorKind.UnterminatedSectionHeader
    // ex.LineNumber: 1
    // ex.LinePosition: 1
    Console.WriteLine(ex.Message);
}
````

Use `TryParse` when an invalid file should not throw. An overload reports the same error without allocating an exception:

````c#
if (CodeOwnersParser.TryParse(content, out IReadOnlyList<CodeOwnersEntry>? entries, out CodeOwnersParseError error))
{
    // ...
}
else
{
    Console.WriteLine(error); // line 1, position 1: the section header is not terminated by ']'
}
````
