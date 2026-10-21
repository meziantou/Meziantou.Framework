# Meziantou.Framework.LLMContext

`Meziantou.Framework.LLMContext` detects known LLM and agentic execution contexts from environment variables. The types are in the `Meziantou.Framework.LLMContext` namespace.

<!-- ref:../../tests/Meziantou.Framework.LLMContext.Tests/LLMContextDetectorTests.cs;region=LLMContextDetector_Detect;format=md-fenced-code-block;dedent=true -->
```csharp
if (LLMContextDetector.IsLLMContext())
{
    IReadOnlyList<LLMContextKind> contexts = LLMContextDetector.Detect();
    Assert.NotEmpty(contexts);
}
```
<!-- endref -->
