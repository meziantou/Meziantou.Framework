namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The performance object of an <see cref="AudioWorkletGlobalScope"/> (https://w3c.github.io/hr-time/#sec-workletperformance):
/// it only measures the time, with the clock and the time origin of the document of the worklet.
/// </summary>
/// <seealso href="https://w3c.github.io/hr-time/#sec-workletperformance"/>
[IdlInterface("WorkletPerformance", "https://w3c.github.io/hr-time/#sec-workletperformance")]
public sealed class WorkletPerformance
{
    private readonly AudioWorkletGlobalScope _global;

    internal WorkletPerformance(AudioWorkletGlobalScope global) => _global = global;

    /// <summary>
    /// https://w3c.github.io/hr-time/#now-method: the time elapsed since the time origin of the global scope (the time origin
    /// of the document of the worklet), in milliseconds.
    /// </summary>
    /// <seealso href="https://w3c.github.io/hr-time/#dom-performance-now"/>
    [IdlMember("now", "https://w3c.github.io/hr-time/#dom-performance-now")]
    public double Now() => _global.GetCurrentHighResolutionTime();
}
