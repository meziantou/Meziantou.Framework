using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Meziantou.Framework;

/// <summary>
/// Initializes COM on the current thread until the scope is disposed. The runtime initializes COM the first time a
/// thread uses a runtime callable wrapper, but not before a direct call to <c>CoCreateInstance</c> or to another COM
/// function, which then fails with <c>CO_E_NOTINITIALIZED</c> on a thread that has not used COM yet.
/// </summary>
[SupportedOSPlatform("windows")]
internal readonly struct ComInitializationScope : IDisposable
{
    // The thread was already initialized with the other concurrency model, which works as well
    private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    private readonly bool _uninitialize;

    private ComInitializationScope(bool uninitialize)
    {
        _uninitialize = uninitialize;
    }

    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "CoInitializeEx exists since Windows 2000, and .NET requires a newer version")]
    public static ComInitializationScope Enter()
    {
        // The apartment of a thread is unknown until COM is initialized. The runtime then uses the multithreaded
        // apartment, so the scope does the same and only uses a single-threaded apartment when the thread asked for one.
        var concurrencyModel = Thread.CurrentThread.GetApartmentState() is ApartmentState.STA
            ? COINIT.COINIT_APARTMENTTHREADED
            : COINIT.COINIT_MULTITHREADED;

        // S_OK and S_FALSE (already initialized with the same model) must both be balanced by CoUninitialize
        var hr = PInvoke.CoInitializeEx(concurrencyModel);
        if (hr.Succeeded)
            return new ComInitializationScope(uninitialize: true);

        if (hr.Value is RPC_E_CHANGED_MODE)
            return new ComInitializationScope(uninitialize: false);

        throw Marshal.GetExceptionForHR(hr.Value)!;
    }

    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "CoUninitialize exists since Windows 2000, and .NET requires a newer version")]
    public void Dispose()
    {
        if (_uninitialize)
        {
            unsafe
            {
                PInvoke.CoUninitialize();
            }
        }
    }
}
