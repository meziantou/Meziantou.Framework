namespace Meziantou.Framework;

/// <summary>
/// Represents an interceptor that can inspect or mutate a <see cref="ProcessWrapper"/> before execution.
/// </summary>
public interface IProcessWrapperInterceptor
{
    /// <summary>
    /// Invoked before process execution starts.
    /// </summary>
    /// <param name="processWrapper">The process wrapper being executed.</param>
    void Intercept(ProcessWrapper processWrapper);
}
