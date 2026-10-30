---
title: Getting started
description: Install the package, detect whether the process runs in an LLM context, and list the detected tools.
---

## Install the package

Add the `Meziantou.Framework.LLMContext` package to a project:

```bash
dotnet add package Meziantou.Framework.LLMContext
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.LLMContext;`.

## Detect an LLM context

`LLMContextDetector.IsLLMContext()` returns `true` when the environment variables of the process match at least one
known tool, and `LLMContextDetector.Detect()` returns the tools it matches:

<!-- ref:../../tests/Meziantou.Framework.LLMContext.Tests/LLMContextDetectorTests.cs;region=LLMContextDetector_Detect;format=md-fenced-code-block;dedent=true -->
```csharp
if (LLMContextDetector.IsLLMContext())
{
    IReadOnlyList<LLMContextKind> contexts = LLMContextDetector.Detect();
    Assert.NotEmpty(contexts);
}
```
<!-- endref -->

## List the detected tools

`Detect` returns an `IReadOnlyList<LLMContextKind>`: one value per tool whose variables are set, or an empty list.
Several tools can be detected at once, for instance when an agent runs inside an editor that is detected too:

<!-- ref:../../tests/Meziantou.Framework.LLMContext.Tests/LLMContextDetectorTests.cs;region=LLMContextDetector_ListContexts;format=md-fenced-code-block;dedent=true -->
```csharp
// Several contexts can be detected at once, for instance an agent started from another one
foreach (LLMContextKind context in LLMContextDetector.Detect())
{
    string name = context switch
    {
        LLMContextKind.ClaudeCode => "Claude Code",
        LLMContextKind.GitHubCopilot => "GitHub Copilot",
        LLMContextKind.GenericAgent => "an agent",
        _ => context.ToString(),
    };

    Assert.NotEmpty(name);
}

// IsLLMContext is true when Detect finds at least one context
Assert.Equal(LLMContextDetector.Detect().Count > 0, LLMContextDetector.IsLLMContext());
```
<!-- endref -->

- The values are always returned in the same order, the order of the `LLMContextKind` enumeration, whatever the order
  of the variables.
- `LLMContextKind.GenericAgent` is not a specific tool: it is the `AGENT_CLI` variable, that any agent can set. See
  [Detection rules](03-DetectionRules.md).
- Each call reads the environment variables again: a variable set or removed with
  `Environment.SetEnvironmentVariable` changes the result of the next call.

## Next steps

- [Detection rules](03-DetectionRules.md): the variables each tool is recognized by.
