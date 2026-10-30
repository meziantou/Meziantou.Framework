---
title: Symbolic links
description: Detect symbolic links, read their targets, and get the canonical path of a file.
---

<!-- ref:../../tests/Meziantou.Framework.FullPath.Tests/FullPathTests.cs;region=FullPath_SymbolicLinks;format=md-fenced-code-block;dedent=true -->
```csharp
FullPath folder = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory());
FullPath file = folder / "data.txt";
File.WriteAllText(file, "");
FullPath link1 = folder / "link1.txt"; // link1.txt -> data.txt
File.CreateSymbolicLink(link1, file);
FullPath link2 = folder / "link2.txt"; // link2.txt -> link1.txt
File.CreateSymbolicLink(link2, link1);

Assert.True(link2.IsSymbolicLink());
Assert.False(file.IsSymbolicLink());

// The target stored in the link, which can be another link
Assert.True(link2.TryGetSymbolicLinkTarget(out FullPath? target));
Assert.Equal(link1, target);

// Follow the links until the target is not a link
Assert.True(link2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.FinalTarget, out FullPath? finalTarget));
Assert.Equal(file, finalTarget);

// The canonical path also resolves the links in the parent folders
Assert.True(link2.TryGetCanonicalPath(out FullPath? canonicalLink));
Assert.True(file.TryGetCanonicalPath(out FullPath? canonicalFile));
Assert.Equal(canonicalFile, canonicalLink);
```
<!-- endref -->

## Detect a link and read its target

- [IsSymbolicLink](cref:Meziantou.Framework.FullPath.IsSymbolicLink) tells whether the path is a symbolic link. It is `false` for the empty path.
- [TryGetSymbolicLinkTarget](cref:Meziantou.Framework.FullPath.TryGetSymbolicLinkTarget) returns the target of the link, as a [FullPath](cref:Meziantou.Framework.FullPath). A relative target is resolved against the
  folder of the link. The method returns `false` when the path is not a symbolic link.

The [SymbolicLinkResolutionMode](cref:Meziantou.Framework.SymbolicLinkResolutionMode) parameter chooses how far to follow the links:

| Mode | Result |
| --- | --- |
| [Immediate](cref:Meziantou.Framework.SymbolicLinkResolutionMode.Immediate) (the default) | The target stored in the link, which can be another link |
| [FinalTarget](cref:Meziantou.Framework.SymbolicLinkResolutionMode.FinalTarget) | The first target that is not a link |
| [AllSymbolicLinks](cref:Meziantou.Framework.SymbolicLinkResolutionMode.AllSymbolicLinks) | The path with the links of every segment resolved, including the parent folders |

[FinalTarget](cref:Meziantou.Framework.SymbolicLinkResolutionMode.FinalTarget) and [AllSymbolicLinks](cref:Meziantou.Framework.SymbolicLinkResolutionMode.AllSymbolicLinks) throw an `IOException` after 40 links, which usually means that the links form a
cycle.

## The canonical path

[TryGetCanonicalPath](cref:Meziantou.Framework.FullPath.TryGetCanonicalPath) returns the final path of an existing file or folder: the operating system resolves the symbolic
links and the reparse points of every segment. It returns `false` when the path does not exist or cannot be resolved.

A link and its target have the same canonical path. Compare canonical paths when links matter, for instance
to check that a path [stays inside a folder](05-ComparingPaths.md#keep-a-path-inside-a-folder) even through a link.
The temporary folder itself can be under a link, as on macOS, where `/var` is a link to `/private/var`: the canonical
path of a file can differ from the path you created it with.
