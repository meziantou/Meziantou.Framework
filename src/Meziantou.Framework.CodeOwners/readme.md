# Meziantou.Framework.CodeOwners

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS file](https://docs.github.com/en/github/creating-cloning-and-archiving-repositories/about-code-owners). These files are common on GitHub and GitLab.

````c#
IReadOnlyList<CodeOwnersEntry> entries = CodeOwnersParser.Parse("* @user1 @user2");
// [0]: CodeOwnersEntry.FromUsername("*", "user1")
// [1]: CodeOwnersEntry.FromUsername("*", "user2")
````

`Parse` throws a `CodeOwnersParseException` when the file is invalid. The exception reports the first error and where it is:

````c#
try
{
    CodeOwnersParser.Parse("[Section\n* @user1");
}
catch (CodeOwnersParseException ex)
{
    // ex.Kind: CodeOwnersErrorKind.UnterminatedSectionHeader
    // ex.LineNumber: 1
    // ex.LinePosition: 1
    Console.WriteLine(ex.Message);
}
````

Use `TryParse` when an invalid file should not throw:

````c#
if (CodeOwnersParser.TryParse(content, out IReadOnlyList<CodeOwnersEntry>? entries))
{
    // ...
}
````
