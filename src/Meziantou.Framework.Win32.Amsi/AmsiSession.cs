using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32;

/// <summary>Represents an AMSI session that correlates multiple scan requests within the same context.</summary>
/// <example>
/// <code>
/// using var context = AmsiContext.Create("MyApplication");
/// using var session = context.CreateSession();
/// if (session.IsMalware("suspicious content", "file1.txt"))
/// {
///     Console.WriteLine("Malware detected in file1.txt");
/// }
/// </code>
/// </example>
[SupportedOSPlatform("windows10.0.10240")]
public sealed class AmsiSession : IDisposable
{
    private readonly AmsiContext _context;
    private readonly AmsiSessionSafeHandle _sessionHandle;
    private bool _disposed;

    internal AmsiSession(AmsiContext context, AmsiSessionSafeHandle session)
    {
        _context = context;
        _sessionHandle = session;
    }

    /// <summary>Scans a string for malware within this session.</summary>
    /// <param name="payload">The string content to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><see langword="true"/> if the content is detected as malware; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the session or its context has been disposed.</exception>
    /// <remarks>Content blocked by administrator policy is not reported as malware. Use <see cref="Scan(string, string)"/> and <see cref="AmsiResultExtensions.ShouldBlock"/> to cover that case.</remarks>
    public bool IsMalware(string payload, string contentName)
    {
        return Scan(payload, contentName).IsMalware();
    }

    /// <summary>Scans a byte buffer for malware within this session.</summary>
    /// <param name="payload">The byte buffer to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><see langword="true"/> if the content is detected as malware; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the session or its context has been disposed.</exception>
    /// <remarks>Content blocked by administrator policy is not reported as malware. Use <see cref="Scan(string, string)"/> and <see cref="AmsiResultExtensions.ShouldBlock"/> to cover that case.</remarks>
    public bool IsMalware(byte[] payload, string contentName)
    {
        return Scan(payload, contentName).IsMalware();
    }

    /// <summary>Scans a string for malware within this session.</summary>
    /// <param name="payload">The string content to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns>The result reported by the antimalware provider.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the session or its context has been disposed.</exception>
    public AmsiResult Scan(string payload, string contentName)
    {
        ThrowIfDisposed();
        Amsi.AmsiScanString(_context._handle, payload, contentName, _sessionHandle, out var result).ThrowOnFailure();
        return result;
    }

    /// <summary>Scans a byte buffer for malware within this session.</summary>
    /// <param name="payload">The byte buffer to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns>The result reported by the antimalware provider.</returns>
    /// <exception cref="COMException">Thrown when the scan operation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the session or its context has been disposed.</exception>
    public AmsiResult Scan(byte[] payload, string contentName)
    {
        ThrowIfDisposed();
        Amsi.AmsiScanBuffer(_context._handle, payload, (uint)payload.Length, contentName, _sessionHandle, out var result).ThrowOnFailure();
        return result;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The session cannot outlive the context it correlates scans for, even though the underlying
        // context handle is kept alive until this session is closed.
        ObjectDisposedException.ThrowIf(_context.IsDisposed, _context);
    }

    public void Dispose()
    {
        _disposed = true;
        _sessionHandle.Dispose();
    }
}
