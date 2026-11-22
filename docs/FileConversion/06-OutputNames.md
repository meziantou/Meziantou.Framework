---
title: Output file names
description: Name the output files with a template, when an input gives several files or when several inputs go to one folder.
---

## The output template

`--output` is a path where placeholders are replaced by the values of each output file:

| Placeholder | Value |
| --- | --- |
| `{name}` | The name of the input file, without its extension |
| `{ext}` | The extension of the output format, without the period |
| `{dir}` | The folder of the input file |
| `{page}` | The number of the page in its input, from 1 |
| `{index}` | The number of the output file, from 1, across all the inputs |
| `{count}` | The number of pages of the input |

```bash
Meziantou.Framework.FileConversion.Tool report.pdf --output "images/{name}/page-{page}.png"
Meziantou.Framework.FileConversion.Tool "photos/*.heic" --output "{dir}/converted/{name}.{ext}" --output-format jpg
```

A relative path is relative to the current folder. The folders of the output files are created when needed. Write
`{{` and `}}` for a brace in a name.

## Numbers

`{page}` and `{index}` are padded with zeros to the number of digits of the number of files, so that the files sort in
order: `page-01.png` to `page-12.png` for 12 pages. When the number of pages is only known after the conversion (an
EPUB or Kindle book laid out as images), they have 4 digits.

A format spec sets the number of digits: `{page:000}` gives `page-001.png`. `{count}` is not padded, unless it has a
format spec too.

## Default names

Without `--output`:

- an input that gives one file is converted to `{dir}/{name}.{ext}`, next to it;
- an input that gives several images, such as a PDF document, is converted to `{dir}/{name}-{page}.{ext}`;
- an input converted to a PDF, comic book archive or TIFF file is converted to `{dir}/{name}.{ext}`. When there are
  several inputs, set `--output` (they all go to that file) or use `--per-input` (each one goes to its own file).
  With `--per-page`, each page goes to its own file, named like an image: `{dir}/{name}-{page}.{ext}`;
- an archive converted to another archive format is converted to `{dir}/{name}.{ext}`: `{name}` is the name without
  its whole extension, such as `backup` for `backup.tar.gz`, and `{ext}` is `tar.gz` for a .tar.gz output.

## Several files with the same name

Two outputs with the same path are an error, which the tool reports before it replaces the first one. Add `{page}` or
`{index}` to a template that names the pages of a document, and `{name}` to a template that names the files of several
inputs.
