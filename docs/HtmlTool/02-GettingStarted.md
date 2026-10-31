---
title: Getting started
description: Install the tool, run a command on a file, and read its output.
---

## Install the tool

Install it as a global tool:

<InstallDotnetTool Id="Meziantou.Framework.Html.Tool" />

The tool needs the ASP.NET Core runtime: the .NET SDK includes it.

## Run a command

The tool has three commands. Each one takes the file to update, `--single-file`, or a glob pattern,
`--file-pattern`:

```bash
meziantou.html append-version --single-file wwwroot/index.html
meziantou.html append-version --file-pattern "**/*.html" --root-directory wwwroot
```

| Command | Description |
| --- | --- |
| `replace-value` | Sets the text of the elements, or the values of the attributes, that an XPath expression selects. See [Replacing values](04-ReplacingValues.md). |
| `append-version` | Appends a version computed from the content of the file to the URLs of local files. See [Appending versions](05-AppendingVersions.md). |
| `inline-resources` | Replaces the references to local scripts, style sheets and images with their content. See [Inlining resources](06-InliningResources.md). |

The files are updated in place: run the tool on a copy, such as the output folder of a build, not on your sources.

## Read the output

For each file, the tool writes the number of nodes it changed:

```text
Updated 3 nodes in 'wwwroot/index.html'
```

It exits with code 0 when it succeeds. A missing required option, an invalid glob pattern or a file that cannot be read
gives a message on the standard error and a non-zero exit code.

`meziantou.html --help` lists the commands, and `meziantou.html <command> --help` the options of a command.

## Next steps

- [Selecting files](03-SelectingFiles.md): update one file or all the files of a folder.
- [Output format and encoding](07-OutputFormatAndEncoding.md): what changes in the files the tool writes.
