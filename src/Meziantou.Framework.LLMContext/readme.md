# Meziantou.Framework.LLMContext

`Meziantou.Framework.LLMContext` detects known LLM and agentic execution contexts from environment variables.

```c#
using Meziantou.Framework.LLMContext;

if (LLMContextDetector.IsLLMContext())
{
    IReadOnlyList<LLMContextKind> contexts = LLMContextDetector.Detect();
}
```
