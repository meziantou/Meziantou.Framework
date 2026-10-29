---
title: Limitations
description: What the template engine, the HTML email templates and the command-line tool do not do.
---

## General

- **.NET 10 and later only.**
- **No trimming and no NativeAOT.** Templates are compiled and loaded while the application runs, which needs the C#
  compiler and the JIT compiler.
- **No sandbox.** The code of a template runs with the permissions of your application. Never compile a template written
  by a user of your application.
- **Templates do not compose.** A template cannot include another template, and there are no layouts or partial
  templates: `include` adds a C# file. Run the templates separately and pass the text of one as an argument of another.
- **No escape sequence for the delimiters.** Write a delimiter from an expression, or choose other delimiters. See
  [Change the delimiters](03-TemplateSyntax.md#change-the-delimiters).
- **Restricted types.** The types of arguments, `OutputType` and the base class must be public, not nested and not
  generic. See [Passing arguments](04-PassingArguments.md#pass-values-of-your-own-types).
- **`dynamic` code is never unloaded.** See [Avoid dynamic code](08-CompilationAndMemory.md#avoid-dynamic-code).
- **A built template is frozen.** It cannot be loaded again or reconfigured: create a new `Template`.
- **Indentation is kept.** The spaces in front of a statement block alone on its line are written. See
  [Line breaks](03-TemplateSyntax.md#line-breaks).
- **Formatting depends on the writer.** Values are formatted with the culture of the writer, which is the current
  culture by default. See [Write a value](03-TemplateSyntax.md#write-a-value).

## HTML emails

- Only the `title` section is returned; the content of other sections is not available to the caller.
- Non-ASCII characters are written as numeric character references by `#html` and `#attr`.
- `#url` encodes a part of a URL, not a complete URL.
- The library generates the HTML body only: no plain-text version, and it does not send emails or attach the images.

## Command-line tool

- No arguments can be passed to the template.
- One template per run.
- T4 templates are read with the syntax of the library: only the delimiters and the `extension` of the `output`
  directive come from T4.
