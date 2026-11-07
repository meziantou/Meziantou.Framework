---
title: Introduction
description: Detect whether the application runs inside an AI coding agent or an LLM tool, from the environment variables they set.
---

`Meziantou.Framework.LLMContext` tells whether the current process runs inside an AI coding agent or another LLM tool,
such as Claude Code, Cursor, GitHub Copilot or Codex, and which one. These tools set environment variables in the
commands they run, and the processes these commands start inherit them: [LLMContextDetector](cref:Meziantou.Framework.LLMContext.LLMContextDetector) recognizes those
variables.

## What you can do with it

- **Detect an agent** with [LLMContextDetector.IsLLMContext](cref:Meziantou.Framework.LLMContext.LLMContextDetector.IsLLMContext), and list the detected tools with
  [LLMContextDetector.Detect](cref:Meziantou.Framework.LLMContext.LLMContextDetector.Detect). See [Getting started](02-GettingStarted.md).
- **Know the variables** each tool is recognized by. See [Detection rules](03-DetectionRules.md).

## When to use it

An agent reads the output of the commands it runs, and cannot answer an interactive prompt. A command-line tool, a
build script or a test framework can use the detection to adapt:

- Write plain text instead of progress bars, spinners and colors, which only add noise to what the agent reads.
- Skip interactive prompts, and fail with a message that says which option to pass instead.
- Give more detailed error messages, or point to the documentation that an agent can read.

The detection relies on environment variables only. It is a hint, not a security boundary: any process can set or
remove these variables. And a process started by an agent, even indirectly, inherits them and is detected too.

## Namespace

[LLMContextDetector](cref:Meziantou.Framework.LLMContext.LLMContextDetector) and [LLMContextKind](cref:Meziantou.Framework.LLMContext.LLMContextKind) are in the [Meziantou.Framework.LLMContext](cref:Meziantou.Framework.LLMContext) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and detect the current context.
