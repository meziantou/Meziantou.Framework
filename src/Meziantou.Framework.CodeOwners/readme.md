# Meziantou.Framework.CodeOwners

`Meziantou.Framework.CodeOwners` parses [CODEOWNERS file](https://docs.github.com/en/github/creating-cloning-and-archiving-repositories/about-code-owners). These files are common on GitHub and GitLab.

````c#
CodeOwnersEntry[] entries = CodeOwnersParser.Parse("* @user1 @user2").ToArray();
// [0]: CodeOwnersEntry.FromUsername("*", "user1")
// [1]: CodeOwnersEntry.FromUsername("*", "user2")
````