---
title: Detection rules
description: The environment variables that identify each tool, and the values that make them match.
---

Each [LLMContextKind](cref:Meziantou.Framework.LLMContext.LLMContextKind) is detected from one or more environment variables. A tool is detected when any of its
variables matches:

| [LLMContextKind](cref:Meziantou.Framework.LLMContext.LLMContextKind) | Variables | Match when |
| --- | --- | --- |
| [ClaudeCode](cref:Meziantou.Framework.LLMContext.LLMContextKind.ClaudeCode) | `CLAUDECODE`, `CLAUDE_CODE_ENTRYPOINT` | Set |
| [Cursor](cref:Meziantou.Framework.LLMContext.LLMContextKind.Cursor) | `CURSOR_EDITOR`, `CURSOR_AI` | Set |
| [Gemini](cref:Meziantou.Framework.LLMContext.LLMContextKind.Gemini) | `GEMINI_CLI` | True |
| [GitHubCopilot](cref:Meziantou.Framework.LLMContext.LLMContextKind.GitHubCopilot) | `GITHUB_COPILOT_CLI_MODE` | True |
| [GitHubCopilot](cref:Meziantou.Framework.LLMContext.LLMContextKind.GitHubCopilot) | `GH_COPILOT_WORKING_DIRECTORY`, `COPILOT_CLI`, `COPILOT_AGENT` | Set |
| [Codex](cref:Meziantou.Framework.LLMContext.LLMContextKind.Codex) | `CODEX_CLI`, `CODEX_SANDBOX` | Set |
| `Aider` | `OR_APP_NAME` | Equal to `Aider` |
| [Plandex](cref:Meziantou.Framework.LLMContext.LLMContextKind.Plandex) | `OR_APP_NAME` | Equal to `plandex` |
| [Amp](cref:Meziantou.Framework.LLMContext.LLMContextKind.Amp) | `AMP_CURRENT_THREAD_ID` | Set |
| [QwenCode](cref:Meziantou.Framework.LLMContext.LLMContextKind.QwenCode) | `QWEN_CODE` | Set |
| [Droid](cref:Meziantou.Framework.LLMContext.LLMContextKind.Droid) | `DROID_CLI` | True |
| [OpenCode](cref:Meziantou.Framework.LLMContext.LLMContextKind.OpenCode) | `OPENCODE_AI` | Set |
| [KimiCLI](cref:Meziantou.Framework.LLMContext.LLMContextKind.KimiCLI) | `KIMI_CLI` | True |
| `OpenHands` | `OR_APP_NAME` | Equal to `OpenHands` |
| [Goose](cref:Meziantou.Framework.LLMContext.LLMContextKind.Goose) | `GOOSE_TERMINAL` | Set |
| [Cline](cref:Meziantou.Framework.LLMContext.LLMContextKind.Cline) | `CLINE_TASK_ID` | Set |
| [RooCode](cref:Meziantou.Framework.LLMContext.LLMContextKind.RooCode) | `ROO_CODE_TASK_ID` | Set |
| [Windsurf](cref:Meziantou.Framework.LLMContext.LLMContextKind.Windsurf) | `WINDSURF_SESSION` | Set |
| [GenericAgent](cref:Meziantou.Framework.LLMContext.LLMContextKind.GenericAgent) | `AGENT_CLI` | True |

- **Set**: the variable exists and its value is not empty. Its value does not matter.
- **True**: the value is `1`, or `true`, `yes` or `on`, ignoring case. `0`, `false`, `no`, `off`, an empty value and
  any other value do not match.
- **Equal to**: the value is the given text, ignoring case.

## Variables that are not used

Some tools also set variables that a developer can set in any shell, such as configuration variables or the variables
of an editor's terminal. They would detect a person as an agent, so they are ignored. For instance, `AMP_HOME`, a
configuration variable, does not detect [Amp](cref:Meziantou.Framework.LLMContext.LLMContextKind.Amp): only `AMP_CURRENT_THREAD_ID`, which Amp sets in the commands it runs,
does.

## Flag your own agent

`AGENT_CLI` is not tied to a tool. Set it to `1` or `true` in the environment of the commands your own agent or
automation runs, and they are detected as [LLMContextKind.GenericAgent](cref:Meziantou.Framework.LLMContext.LLMContextKind.GenericAgent).
