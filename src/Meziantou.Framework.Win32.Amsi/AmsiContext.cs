using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32;

/// <summary>Provides access to the Windows Antimalware Scan Interface (AMSI) for scanning content for malware.</summary>
/// <example>
/// <code>
/// using var context = AmsiContext.Create("MyApplication");
/// if (context.IsMalware("X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "test.txt"))
/// {
///     Console.WriteLine("Malware detected!");
/// }
/// </code>
/// </example>
[SupportedOSPlatform("windows10.0.10240")]
public sealed class AmsiContext : IDisposable
{
    internal readonly AmsiContextSafeHandle _handle;

    private static readonly AmsiSessionSafeHandle DefaultSession = new();

    private bool _disposed;

    /// <summary>Gets a value indicating whether this context has been disposed.</summary>
    /// <remarks>
    /// Tracked separately from the handle: a session holds a reference on the handle, so
    /// <see cref="System.Runtime.InteropServices.SafeHandle.IsClosed"/> stays <see langword="false"/>
    /// after <see cref="Dispose"/> until every session is closed.
    /// </remarks>
    internal bool IsDisposed => _disposed;

    private AmsiContext(AmsiContextSafeHandle context)
    {
        _handle = context;
    }

    /// <summary>Creates a new AMSI context for the specified application.</summary>
    /// <param name="applicationName">The name of the application that will use the AMSI context.</param>
    /// <returns>A new <see cref="AmsiContext"/> instance.</returns>
    /// <exception cref="COMException">Thrown when the AMSI context cannot be initialized.</exception>
    public static AmsiContext Create(string applicationName)
    {
        Amsi.AmsiInitialize(applicationName, out var context).ThrowOnFailure();
        return new AmsiContext(context);
    }

    /// <summary>Creates a new AMSI session for correlating multiple scan requests.</summary>
    /// <returns>A new <see cref="AmsiSession"/> instance.</returns>
    /// <exception cref="COMException">Thrown when the AMSI session cannot be opened.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the context has been disposed.</exception>
    public AmsiSession CreateSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Amsi.AmsiOpenSession(_handle, out var session).ThrowOnFailure();
        return new AmsiSession(this, session);
    }

    /// <summary>Scans a string for malware using the Windows antimalware provider.</summary>
    /// <param name="payload">The string content to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><see langword="true"/> if the content is detected as malware; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the context has been disposed.</exception>
    /// <remarks>Content blocked by administrator policy is not reported as malware. Use <see cref="Scan(string, string)"/> and <see cref="AmsiResultExtensions.ShouldBlock"/> to cover that case.</remarks>
    public bool IsMalware(string payload, string contentName)
    {
        return Scan(payload, contentName).IsMalware();
    }

    /// <summary>Scans a byte buffer for malware using the Windows antimalware provider.</summary>
    /// <param name="payload">The byte buffer to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><see langword="true"/> if the content is detected as malware; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the context has been disposed.</exception>
    /// <remarks>Content blocked by administrator policy is not reported as malware. Use <see cref="Scan(string, string)"/> and <see cref="AmsiResultExtensions.ShouldBlock"/> to cover that case.</remarks>
    public bool IsMalware(byte[] payload, string contentName)
    {
        return Scan(payload, contentName).IsMalware();
    }

    /// <summary>Scans a string for malware using the Windows antimalware provider.</summary>
    /// <param name="payload">The string content to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns>The result reported by the antimalware provider.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the context has been disposed.</exception>
    public AmsiResult Scan(string payload, string contentName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Amsi.AmsiScanString(_handle, payload, contentName, DefaultSession, out var result).ThrowOnFailure();
        return result;
    }

    /// <summary>Scans a byte buffer for malware using the Windows antimalware provider.</summary>
    /// <param name="payload">The byte buffer to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns>The result reported by the antimalware provider.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the context has been disposed.</exception>
    public AmsiResult Scan(byte[] payload, string contentName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Amsi.AmsiScanBuffer(_handle, payload, (uint)payload.Length, contentName, DefaultSession, out var result).ThrowOnFailure();
        return result;
    }

    public void Dispose()
    {
        _disposed = true;
        _handle.Dispose();
    }
}
