---
title: Getting started
description: Install the file conversion tool, convert an image, and list the formats and their options.
---

## Install the tool

Install it as a global tool:

<InstallDotnetTool Id="Meziantou.Framework.FileConversion.Tool" />

## Convert a file

Give the input files, and the output format with `--output-format` or with the extension of `--output`:

```bash
Meziantou.Framework.FileConversion.Tool photo.png --output-format jpg
Meziantou.Framework.FileConversion.Tool photo.png --output photo.avif
```

Without `--output`, the output file is written next to the input, with the extension of the format: the first command
writes `photo.jpg`. The tool prints the path of each file it writes.

## Convert several files

Give several files, or glob patterns. The tool expands the patterns itself, so they work in every shell when they are
quoted. The files a pattern matches are sorted in natural order: `page2.jpg` comes before `page10.jpg`.

```bash
Meziantou.Framework.FileConversion.Tool "scans/*.jpg" --output book.pdf
Meziantou.Framework.FileConversion.Tool "photos/**/*.heic" --output-format jpg
```

The first command writes one PDF document with one page per image. The second one writes a JPEG file next to each
HEIC file.

## List the formats

`--list-formats` prints the formats, their extensions, whether the tool reads or writes them, and their options:

```bash
Meziantou.Framework.FileConversion.Tool --list-formats
```

Use the name or an extension of a format with `--output-format` and `--input-format`: `jpeg`, `jpg` and `.jpg` are the
same format. `--input-format` is only needed when the content of a file does not tell its format.

## Existing files and errors

The tool does not replace an existing file: use `--overwrite` to replace it. An output that would replace an input
is always an error.

The tool exits with code 0 when every file is converted. An error, such as an input that does not exist, a file that
is not valid, or two outputs with the same path, gives exit code 1 and a message on the standard error.
