---
title: Prompts
description: Ask a yes/no question in the console, with an optional default answer and custom labels.
---

## Ask a yes/no question

<!-- ref:../../samples/Meziantou.Framework.CommandLine.Samples/ReadmeExamples.cs;region=Prompt_YesNo;format=md-fenced-code-block;dedent=true -->
```csharp
// Simple yes/no prompt with default value
var proceed = Prompt.YesNo("Do you want to continue?", defaultValue: true);
// Displays: Do you want to continue? [Y/n]
// User can press Enter to use default (true)

// Without default value
var confirm = Prompt.YesNo("Are you sure?", defaultValue: null);
// Displays: Are you sure? [y/n]
// User must enter y or n

// Custom yes/no labels
var delete = Prompt.YesNo("Delete file?", "Yes", "No", defaultValue: false);
// Displays: Delete file? [y/N]

// Prompts are case-insensitive and loop until valid input
var result = Prompt.YesNo("Enable feature?", defaultValue: true);
if (result)
{
    Console.WriteLine("Feature enabled!");
}
```
<!-- endref -->

`Prompt.YesNo` writes the question followed by the two answers in brackets, then reads a line from the console:

- The answers are compared without case: `y`, `Y`, `yes` with the `Yes` label, and so on.
- An empty line returns the default value. Without a default value (`null`), the question is asked again.
- Any other answer asks the question again, until the user gives a valid one.

With the overload that takes only a default value, the labels are `y` and `n`, and the default answer is shown in upper
case: `[Y/n]` when the default is `true`, `[y/N]` when it is `false`, `[y/n]` when there is none. With custom labels, the
labels are shown as you write them: write the default answer in upper case to show it.

## Closed input

When the standard input is closed, for instance when it is redirected from an empty file or the application runs
without a console, `YesNo` returns the default value. Without a default value it throws an `EndOfStreamException` rather
than waiting forever: pass a default value when the application can run unattended.
