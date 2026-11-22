# Mirror of the test corpora

Many tests read third-party corpora: test suites, conformance files and npm packages, pinned in this repository and
downloaded from their upstream repositories and servers. They are too large to be committed, and the tests would fail
if an upstream repository were deleted, rewritten or unavailable. So each library has a mirror repository on GitHub
that holds a snapshot of each pin. The fetchers read the snapshots first and fall back to upstream.

| Library | Fetcher | Lock file | Mirror repository |
|---|---|---|---|
| Meziantou.Framework.Pdf | `dotnet run eng/pdf/fetch-tests.cs` | `tests/Meziantou.Framework.Pdf.Corpus/corpus.mirror.json` | [meziantou/test-corpus-pdf](https://github.com/meziantou/test-corpus-pdf) |
| Meziantou.Framework.JavaScriptEngine | `dotnet run eng/javascriptengine/fetch-tests.cs` | `eng/javascriptengine/corpus.mirror.json` | [meziantou/test-corpus-javascriptengine](https://github.com/meziantou/test-corpus-javascriptengine) |
| Meziantou.Framework.WebPlatform | `dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- fetch-tests` | `eng/webplatform/corpus.mirror.json` | [meziantou/test-corpus-webplatform](https://github.com/meziantou/test-corpus-webplatform) |
| Meziantou.Framework.Imaging | `dotnet run eng/imaging/fetch-tests.cs` | `eng/imaging/corpus.mirror.json` | [meziantou/test-corpus-imaging](https://github.com/meziantou/test-corpus-imaging) |
| Meziantou.Framework.WasmEngine | `dotnet run eng/wasmengine/fetch-tests.cs` | `eng/wasmengine/corpus.mirror.json` | [meziantou/test-corpus-wasmengine](https://github.com/meziantou/test-corpus-wasmengine) |

## Snapshots

A snapshot is a git commit without parent, tagged `<name>/<snapshot>` in the mirror repository:

- **A git suite** (test262, web-platform-tests, the PDF corpora, the WebAssembly test suite, ...): the files of the pinned paths of the upstream
  commit, with their modes. A snapshot of web-platform-tests also has the manifest of its commit (`MANIFEST.json.gz`).
- **Downloaded files** (npm tarballs, the JPEG XL and JPEG-LS conformance files): the files, named by their hash.

The fetchers check out a git suite the way they check out an upstream commit: a shallow, blob-less, sparse clone. The
files of a snapshot have the same objects as the files of the upstream commit, so the two sources alternate without
downloading the files again, and changing a pin only downloads the files that changed. Downloaded files are checked out
in the folder where the fetcher or the tests look for them before downloading them.

A snapshot is deterministic: fixed author, dates and message, no signature. The same pin always gives the same commit,
so anyone can create a snapshot again from upstream and compare it with the one of the mirror.

## Lock files

A lock file (`corpus.mirror.json`) names the mirror repository and maps each pin to its snapshot commit. A pin is a list
of strings that identifies the content: the upstream commit and the paths of a git suite, or the SHA-256 of the file
that pins downloaded files (with LF line endings). git checks the objects it receives against their hashes, so the
snapshot commit pins the content: a fetcher rejects a tag of the mirror that is not the commit of the lock file.

## Fallback

A fetcher fetches a corpus from upstream, with a warning (an annotation in GitHub Actions), when:

- the lock file has no snapshot for the pin (the pin changed, and the snapshots were not published),
- the mirror cannot be fetched,
- or the tag of the snapshot is not the commit of the lock file.

When the mirror has no downloaded files, the fetcher or the tests download them from upstream, as before the mirror.

## Publishing

After a change of a pin, `--publish` fetches the corpora that the mirror has no snapshot of from upstream, creates their
snapshots, pushes them, and writes the lock file, to commit with the change of the pin. It fetches the other corpora from
the mirror, so publishing does not depend on the upstream of the pins that did not change:

```shell
dotnet run eng/pdf/fetch-tests.cs -- --publish
dotnet run eng/javascriptengine/fetch-tests.cs -- --publish
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- fetch-tests --publish
dotnet run eng/imaging/fetch-tests.cs -- --publish
dotnet run eng/wasmengine/fetch-tests.cs -- --publish
```

Pushing requires the right to write to the mirror repository. A ruleset of each mirror repository blocks moving and
deleting tags, so a published snapshot stays available to the commits that pin it. `--mirror <owner/name|url|path>`
uses another mirror repository: a fork, or a local repository to test the mirror. To create a snapshot again from
upstream and compare it with the one of the mirror, remove its entry from the lock file first.

The mirror repositories redistribute the corpora under their upstream licenses: pin the license file of a repository
with its other paths when it has one.

### Publishing from CI

The `publish-test-corpora` workflow (`.github/workflows/publish-test-corpora.yml`) runs on main after a change of a pin,
and can be run manually on main. It publishes the snapshots that the mirrors do not have, and opens a pull request with
the lock files. So a pull request that changes a pin without publishing (from a fork, or by someone who cannot push to
the mirrors) falls back to upstream only until that pull request is merged. The weekly update of the WebPlatform
specifications (`update-webplatform-specs.yml`) publishes the snapshots of the pins it changes, so its pull request has
the lock file.

The workflows push with the `TEST_CORPUS_TOKEN` secret: a fine-grained personal access token of the owner of the mirror
repositories, with the "Contents: read and write" permission on them only. Without it, the update workflow publishes
nothing, with a warning, and `publish-test-corpora` fails.
