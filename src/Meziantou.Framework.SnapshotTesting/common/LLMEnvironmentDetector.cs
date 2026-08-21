using Meziantou.Framework.LLMContext;

namespace Meziantou.Framework;

/// <summary>
/// Caches whether the process runs in a known LLM or agentic context. <see cref="LLMContextDetector.IsLLMContext" />
/// reads about thirty environment variables and allocates on every call, and it sits next to
/// <see cref="BuildServerDetector" /> and <see cref="ContinuousTestingDetector" />, which are both resolved
/// once for the lifetime of the process.
/// </summary>
internal static class LLMEnvironmentDetector
{
    public static bool Detected { get; } = LLMContextDetector.IsLLMContext();
}
