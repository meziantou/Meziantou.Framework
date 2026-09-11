# Meziantou.Framework.Globbing

## Supported glob features

- `*` matches any number of characters including none
- `?` matches a single character
- `[abc]` matches one character given in the bracket
- `[!abc]` matches any character not in the brackets
- `[a-z]` matches one character from the range given in the bracket
- `[!a-z]` matches one character not in the range given in the bracket
- `{abc,123}` matches one of the literals
- `**` matches zero or more directories

## Dialects

| Dialect | Follows | Notes |
| --- | --- | --- |
| `Standard` | the syntax above | Wildcards skip names starting with a dot unless `GlobOptions.MatchLeadingDot` is set |
| `Git` | gitignore(5) and git's wildmatch | `[^a]`, `[[:alpha:]]`, `\` escapes in brackets; `{}` are literal; use `GlobCollection.ParseGitIgnore` for a whole `.gitignore` file |
| `MSBuild` | MSBuild item specs | `/` and `\` are separators, `%XX` escapes, `*.*` matches every file, a leading `..` is kept |
| `Posix` | `fnmatch(3)` without flags | Matches a plain string: `*` and `?` match `/`; `[[:alpha:]]`, `[[=a=]]`, `[[.-.]]`, `[^a]` |
| `PosixPath` | `fnmatch(3)` with `FNM_PATHNAME` | Same syntax as `Posix`, but a `/` is only matched by a `/` |

The `Git`, `MSBuild`, `Posix` and `PosixPath` dialects are tested against corpora produced by git, MSBuild and
fnmatch. Characters are compared as UTF-16 code units, whereas git compares the bytes of their UTF-8 encoding.

## Usage

Install the NuGet package `Meziantou.Framework.Globbing` ([NuGet](https://www.nuget.org/packages/Meziantou.Framework.Globbing/))

````bash
dotnet package add Meziantou.Framework.Globbing
````

- `IsMatch` tests whether a file matches the glob pattern

    ````csharp
    Glob glob = Glob.Parse("src/**/*.txt", GlobDialect.Standard, GlobOptions.IgnoreCase);
    glob.IsMatch("src/abc.txt");
    ````

- Enumerate files that match a glob pattern

    ````csharp
    // Enumerate files that match the glob in the folder rootDirectory
    Glob glob = Glob.Parse("src/**/*.txt", GlobDialect.Standard);
    foreach(var file in glob.EnumerateFiles("rootDirectory"))
    {
        Console.WriteLine(file);
    }
    ````

## Addition resources

- [Enumerating files using Globbing and System.IO.Enumeration](https://www.meziantou.net/enumerating-files-using-globbing-and-system-io-enumeration.htm)
