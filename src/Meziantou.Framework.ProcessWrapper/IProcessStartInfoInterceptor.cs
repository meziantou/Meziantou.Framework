using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>
/// Represents an interceptor that can inspect or mutate the <see cref="ProcessStartInfo"/> before process creation.
/// </summary>
public interface IProcessStartInfoInterceptor
{
    /// <summary>
    /// Invoked before a process is created from the start information.
    /// </summary>
    /// <param name="processWrapper">The process wrapper being executed.</param>
    /// <param name="processStartInfo">The process start information that will be used to create the process.</param>
    void Intercept(ProcessWrapper processWrapper, ProcessStartInfo processStartInfo);
}
