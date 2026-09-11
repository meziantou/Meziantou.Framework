# Globbing conformance corpora

`GlobConformanceTests` runs these files. Their expected results were not written by hand: each one was produced by
the reference implementation of a dialect, over randomly generated patterns and paths, and then sampled so that
every combination of features (bracket expressions, negations, escapes, classes, ranges, separators, dots…) is
represented.

| File | Dialects | Reference implementation |
| --- | --- | --- |
| `fnmatch-conformance.txt` | `Posix`, `PosixPath` | `fnmatch(3)` with the flags `0` and `FNM_PATHNAME`, from glibc 2.39 (`C.UTF-8`) and the macOS libc |
| `gitignore-conformance.json` | `Git`, through `GlobCollection.ParseGitIgnore` | `git check-ignore --no-index` (git 2.55, `core.ignorecase=false`) over a real directory tree |
| `msbuild-conformance.json` | `MSBuild` | the items of a real MSBuild evaluation (`dotnet msbuild -getItem`, MSBuild 18.11) over a real directory tree |

## `fnmatch-conformance.txt`

One case per line: `pattern`, `text`, the expected result for `Posix` and the expected result for `PosixPath`,
separated by tabs. A result is `1` (match), `0` (no match), or `-` when the case does not apply to the dialect.
A pattern that the library rejects must have `0`: an invalid pattern cannot match anything.

Only the results on which glibc and the macOS libc agree are kept. They disagree on what POSIX leaves unspecified,
and on an unterminated `[`, which POSIX reads as an ordinary character (glibc does, the macOS libc does not).

A few agreed results are `-` on purpose, because the library deliberately differs from `fnmatch`:

- `PosixPath` matches paths rather than strings: a path ending with `/` is a directory, so `a/*` does not match
  `a/`, and an empty path matches nothing.
- An unknown character class (`[[:foo:]]`) makes the pattern invalid. fnmatch still matches a character that an
  earlier item of the same bracket expression matched, as it stops reading the expression there.
- glibc reports no match for an unterminated bracket expression that ends with `-`, and for `*\/` with
  `FNM_PATHNAME`. POSIX reads the first as an ordinary `[`, and `\/` as a `/`.
- POSIX leaves a range that ends with a character class, such as `[a-[:digit:]]`, unspecified.

## `gitignore-conformance.json`

`entries` lists the files and directories of the tree, and each case gives the content of a `.gitignore` file at
its root with the indexes of the entries that git ignores. The paths are ASCII: git compares the bytes of the UTF-8
encoding (`?` does not match `é`, which is two bytes), whereas the library compares characters.

## `msbuild-conformance.json`

`files` lists the files of the tree, and each case gives an `Include` value with the indexes of the files it
evaluates to. MSBuild ignores the case on this file system, so the tests use `GlobOptions.IgnoreCase`. The cases where
MSBuild is inconsistent with itself are not part of the corpus: a file spec that MSBuild cannot expand (`a**`, or a
`..` after a wildcard) becomes a literal item, a file name ending with `.` depends on the code path, and `**/**/**`
skips the files at the root while `**/**` does not.
