---
title: Introduction
description: What AnsiTextProcessor does, and the escape sequences it recognizes.
---

Command-line tools write ANSI escape sequences to color and style their output: `ESC[31m` turns the text red, `ESC[1m`
makes it bold, `ESC[0m` resets the style. A terminal interprets them, but a log file, a test assertion or a web page
shows them as garbage. `Meziantou.Framework.AnsiFormatting` removes these sequences, or reads the styles they describe.

## What you can do with it

- **Remove the escape sequences** from the output of a process before you log it, compare it, or parse it. See
  [Getting started](02-GettingStarted.md).
- **Read the text and its styles**: bold, italic, underline, inverse, and the foreground and background colors of each
  part of the text, for instance to display colored output in a web page. See [Reading styles](03-ReadingStyles.md).

## Supported sequences

`AnsiTextProcessor` recognizes two families of escape sequences:

- **CSI** sequences start with `ESC [` and end with a character between `@` and `~` (`0x40`-`0x7E`). They include the
  SGR sequences that set the style, such as `ESC[1;31m`, and the sequences that move the cursor or erase the line.
- **OSC** sequences start with `ESC ]` and end with `BEL` or `ESC \`. They include hyperlinks (`OSC 8`) and window titles
  (`OSC 0` and `OSC 2`).

Only SGR sequences, the CSI sequences that end with `m`, describe styles. The other sequences are removed and carry no
style.

## Namespaces

`AnsiTextProcessor` is in the [Meziantou.Framework](cref:Meziantou.Framework.AnsiFormatting!N:Meziantou.Framework) namespace. The types of the parsed text (`AnsiText`, `AnsiTextRun`,
`AnsiStyle`, `AnsiColor` and `AnsiColorKind`) are nested in `AnsiTextProcessor`.
