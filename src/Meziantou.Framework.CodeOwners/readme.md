# Meziantou.Framework.CodeOwners

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS file](https://docs.github.com/en/github/creating-cloning-and-archiving-repositories/about-code-owners). These files are common on GitHub and GitLab.

Parsing takes the dialect of the file, because the two hosts do not accept the same syntax: sections (`[Name]`, `^[Name]`, `[Name][2]`) are a GitLab extension, and in the GitHub syntax a line starting with `[` is a pattern, since `[` opens a character class.

Each entry is one line of the file: a pattern and the owners it declares.

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
CodeOwnersFile file = CodeOwnersFile.Parse("* @user1 docs@example.com", CodeOwnersDialect.GitHub);
Assert.Equal("*", file.Entries[0].Pattern);
Assert.Equal(CodeOwnerType.Username, file.Entries[0].Owners[0].Type);
Assert.Equal("user1", file.Entries[0].Owners[0].Name);
Assert.Equal(CodeOwnerType.EmailAddress, file.Entries[0].Owners[1].Type);
Assert.Equal("docs@example.com", file.Entries[0].Owners[1].Name);
```
<!-- endref -->

Entries are returned in file order, and CODEOWNERS resolution is last-match-wins, so the owners of a path are those of the last entry whose pattern matches it:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_LastMatchWins;format=md-fenced-code-block;dedent=true -->
```csharp
var file = CodeOwnersFile.Parse("""
    * @user1
    *.md @docs-team
    CHANGELOG.md
    """, CodeOwnersDialect.GitHub);

IReadOnlyList<CodeOwner> GetOwners(string path)
{
    CodeOwnersEntry? owningEntry = file.Entries.LastOrDefault(entry => Matches(entry.Pattern, path));
    return owningEntry?.Owners ?? [];
}

Assert.Equal("user1", Assert.Single(GetOwners("src/Program.cs")).Name);
Assert.Equal("docs-team", Assert.Single(GetOwners("README.md")).Name);

// An empty Owners list means the entry explicitly leaves the pattern unowned
Assert.Empty(GetOwners("CHANGELOG.md"));

// The library does not match patterns: this matcher only handles the patterns of this example
static bool Matches(string pattern, string path)
    => pattern == "*" || pattern == path || (pattern.StartsWith("*.", StringComparison.Ordinal) && path.EndsWith(pattern[1..], StringComparison.Ordinal));
```
<!-- endref -->

`Parse` throws a `CodeOwnersParseException` when the file is invalid. The exception reports the first error and where it is:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_ParseException;format=md-fenced-code-block;dedent=true -->
```csharp
var ex = Assert.Throws<CodeOwnersParseException>(() => CodeOwnersFile.Parse("[Section\n* @user1", CodeOwnersDialect.GitLab));
Assert.Equal(CodeOwnersParseErrorKind.UnterminatedSectionHeader, ex.Error.Kind);
Assert.Equal(1, ex.Error.LineNumber);
Assert.Equal(1, ex.Error.LinePosition);
Assert.Equal("The CODEOWNERS file is invalid at line 1, position 1: the section header is not terminated by ']'", ex.Message);
```
<!-- endref -->

Use `TryParse` when an invalid file should not throw. An overload reports the same error without allocating an exception:

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
var content = "[Section\n* @user1";
if (CodeOwnersFile.TryParse(content, CodeOwnersDialect.GitLab, out CodeOwnersFile? file, out CodeOwnersParseError error))
{
    Assert.Fail("The content is not a valid GitLab CODEOWNERS file");
}
else
{
    Assert.Null(file);
    Assert.Equal("line 1, position 1: the section header is not terminated by ']'", error.ToString());
}
```
<!-- endref -->

`Parse` and both `TryParse` overloads also accept a `ReadOnlySpan<char>`, so a file read into a buffer does not have to be turned into a `string` first. The parsed `CodeOwnersFile` never references the span: every value it exposes is copied out of it, and reported error positions are relative to the start of the span.

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_ParseSpan;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new char[4096];
using var reader = new StringReader("*.md @docs-team"); // For instance, File.OpenText("CODEOWNERS")
var length = reader.ReadBlock(buffer);

CodeOwnersFile file = CodeOwnersFile.Parse(buffer.AsSpan(0, length), CodeOwnersDialect.GitHub);
Assert.Equal("*.md", file.Entries[0].Pattern);
```
<!-- endref -->

A `CodeOwnersFile` only exists for a valid file: neither method hands back a partially parsed one. `Entries`, `Owners` and `DefaultOwners` are read-only views that cannot be cast back to a mutable list.

In the GitLab dialect an owner can also be a role, written `@@developer`, `@@maintainer` or `@@owner` (plural accepted). Anything else after `@@` is an error, as is a `@` inside a username.

<!-- ref:../../tests/Meziantou.Framework.CodeOwners.Tests/CodeOwnersParserTests.cs;region=CodeOwners_Role;format=md-fenced-code-block;dedent=true -->
```csharp
CodeOwnersFile file = CodeOwnersFile.Parse("*.md @@maintainers", CodeOwnersDialect.GitLab);
Assert.Equal(CodeOwnerType.Role, file.Entries[0].Owners[0].Type);
Assert.Equal("maintainers", file.Entries[0].Owners[0].Name);
```
<!-- endref -->
