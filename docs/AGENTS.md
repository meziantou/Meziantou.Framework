# Documentation

This folder holds the content of the documentation site of the libraries of this repository. It is written for the
people who use the libraries: how to do things with them, not how they are implemented. The site is not generated or
published yet; only the Markdown content lives here.

These instructions complete the repository-level `AGENTS.md`.

## Layout

```
docs/
  <nn>-<Library>/
    _folder.yml
    <nn>-<Page>.md
    <nn>-<Group>/
      _folder.yml
      01-Overview.md
      <nn>-<Page>.md
      images/<Example>.png
```

- The number prefix sets the order of the sections, groups and pages; the name after it is PascalCase
  (`02-GettingStarted.md`, `05-LoadingAndSaving/`). Numbers have two digits (`01`, `02`, ... `10`), start at `01` and
  have no gaps.
- A library is one section (`01-Imaging/`). A new library gets a new section that starts with `01-Introduction.md` and
  `02-GettingStarted.md`.
- A group folder opens with `01-Overview.md`, which introduces the group and links to its pages.
- Renumbering or renaming a page or a folder changes its links: update every link to it in the same change.
- `AGENTS.md` files are instructions, not pages.

## Folder names

The folder names are not shown on the site. Every section and group folder has a `_folder.yml` file that gives its
display name in `title` (required) and a one-sentence `description` (recommended), like the front matter of a page:

```yaml
title: Loading and saving
description: Load images from files, streams and memory, identify them, save them, and protect your application.
```

The `images` folders have none: they are not shown.

`packageIds` lists the NuGet packages the folder documents, as a block list sorted ordinally, after `description`:

```yaml
title: DNS
description: Query DNS servers over UDP, TCP, TLS, HTTPS and QUIC, serve your own records, filter domains with block lists, and run a filtering DNS proxy.
packageIds:
- Meziantou.DnsProxy
- Meziantou.Framework.DnsClient
- Meziantou.Framework.DnsFilter
- Meziantou.Framework.DnsServer
```

- A section requires it. Every package of `src/` (the projects that are not `IsPackable=false`; the id is the project
  file name) is listed by exactly one section, so a new package is added to the list of its section.
- A group may have it when its pages are about some of the packages of its section, for instance
  `73-Dns/03-DnsClient/` lists `Meziantou.Framework.DnsClient`. It can only list packages of its parent. Without it,
  the group covers the packages of its parent.
- Third-party packages the pages install (`Npgsql`) are not listed.
- `dotnet run ./eng/update-all.cs` (or `-- --step docs`) checks the lists.

## Front matter

Every page starts with a YAML front matter. `title` is required, `description` (one sentence, shown in lists and search
results) is recommended:

```yaml
---
title: Resizing images
description: Resize an image or an animation, keep the aspect ratio, letterbox or crop to fill.
---
```

The page body does not repeat the title as a `#` heading: the site shows the `title`. Sections of a page start at `##`.

## Writing

- Task first: start with what the reader wants to do and the code that does it, then the options, then the details
  and pitfalls. One topic per page; split a page that covers two.
- Facts must match the library. The library's `readme.md` (for Imaging, `src/Meziantou.Framework.Imaging/readme.md`)
  and its XML documentation are the reference: check every statement against them, and never promise more than they
  do. When the behavior of a library changes, its pages change in the same change.
- Link to other pages with relative paths, including the number prefixes:
  `[resource limits](../05-LoadingAndSaving/07-ResourceLimits.md)`. Do not link to the library readme for things a page
  covers.
- No benchmark numbers or comparisons with other libraries: they go stale. Give guidance instead.

## Code examples

C# code is never written by hand. As in the readme files of `src/`, every example is copied from code that compiles by
[Meziantou.FileReferencer](https://github.com/meziantou/Meziantou.FileReferencer). Write
`<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Letterbox;format=md-fenced-code-block;dedent=true -->`
and `<!-- endref -->` on two lines, each alone on its line, with nothing between them; the path is relative to the page.
(They are inline code here so that the tool does not process this file.)

- Imaging examples are `#region Imaging_<Scenario>` blocks of `samples/Meziantou.Framework.Imaging.Samples/*.cs`.
  Reuse the regions of the readme; add a new region to the `<Area>Examples.cs` file of the group (for instance
  `DrawingExamples.cs`). These files are linked into `tests/Meziantou.Framework.Imaging.Tests`, where
  `Documentation/DocumentationExampleTests.cs` runs every example and checks what the page says about it.
- A region is self-contained: it is read as a standalone snippet.
- `Documentation/DocumentationSnippetTests.cs` fails on a C# block that is not generated from a region, on a page
  without a `title`, with a broken relative link or with a missing image, on a name without a two-digit prefix, and on
  a folder without a `_folder.yml` that has a `title`.
- Shell commands (`dotnet add package …`) are the only code written inline.
- Run `git add -N docs samples` (the tool only sees files Git tracks) then `dotnet run ./eng/update-all.cs` (or
  `-- --step snippets`) to fill the blocks. Never edit the generated content.

## Images

A drawing or text example is followed by the image it produces, so the reader sees what the code does:

```markdown
![A sky gradient, a sun and an outlined hill in a frame](images/DrawLandscape.png)
```

- Images are generated by `tests/Meziantou.Framework.Imaging.Tests/Documentation/DocumentationImageTests.cs`, which
  runs the example and compares its pixels with the committed PNG. Never edit an image by hand.
- To add an image, add a test to that class, then run it with `MEZIANTOU_IMAGING_UPDATE_DOCS_IMAGES=1` to write the PNG
  (the same command refreshes the images after a change of the rendering), and run it again without the variable:

  ```bash
  MEZIANTOU_IMAGING_UPDATE_DOCS_IMAGES=1 dotnet test --project tests/Meziantou.Framework.Imaging.Tests -- --filter-class "*DocumentationImageTests"
  ```

- Look at every new or changed image: it must show what the page says.
- The alt text describes what the image shows, not the code.
- Keep images small (a few hundred pixels wide). Examples that depend on the machine (installed fonts) get no image.
