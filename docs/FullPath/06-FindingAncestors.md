---
title: Finding ancestors
description: Find the root of the Git repository or the nearest parent folder that matches a condition.
---

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_FindAncestors;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath repository = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory());
Directory.CreateDirectory(repository / ".git");
FullPath project = repository / "src" / "App";
Directory.CreateDirectory(project);
File.WriteAllText(project / "App.csproj", "<Project />");
FullPath program = project / "Program.cs";

// The folder that contains .git (a folder, or a file in a worktree)
Assert.True(program.TryFindGitRepositoryRoot(out FullPath root));
Assert.Equal(repository, root);

// The nearest folder that matches a condition
Assert.True(program.TryFindFirstAncestor(path => Directory.EnumerateFiles(path, "*.csproj").Any(), out FullPath projectFolder));
Assert.Equal(project, projectFolder);
```
<!-- endref -->

## Find the root of a Git repository

[TryFindGitRepositoryRoot](cref:Meziantou.Framework.FullPath.TryFindGitRepositoryRoot) returns the nearest folder, starting from the path itself, that contains a `.git` entry.
The entry can be a folder, or a file as in a Git worktree or a submodule. When the path is an existing file, the search
starts from its folder.

[FindRequiredGitRepositoryRoot](cref:Meziantou.Framework.FullPath.FindRequiredGitRepositoryRoot) returns the same folder, or throws an `InvalidOperationException` when the path is not
in a Git repository. Tests and build tools use it to find the files of the repository from the current directory:
`FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot()`.

## Find a folder that matches a condition

- [TryFindFirstAncestorOrSelf](cref:Meziantou.Framework.FullPath.TryFindFirstAncestorOrSelf) tests the path itself, then each of its parents, up to the root folder.
- [TryFindFirstAncestor](cref:Meziantou.Framework.FullPath.TryFindFirstAncestor) starts from the parent: the path itself is never returned.

Both return `false`, and the empty path, when no folder matches. The condition receives a [FullPath](cref:Meziantou.Framework.FullPath): it can check the
file system, as in the example, or the path only, such as `path => path.Name == "src"`.
