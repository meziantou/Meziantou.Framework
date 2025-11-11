using System.ComponentModel;
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
[SupportedOSPlatform("windows")]
public sealed class AmsiSession : IDisposable
{
    private readonly AmsiContext _context;
    private readonly AmsiSessionSafeHandle _sessionHandle;

    internal AmsiSession(AmsiContext context, AmsiSessionSafeHandle session)
    {
        _context = context;
        _sessionHandle = session;
    }

    /// <summary>Scans a string for malware within this session.</summary>
    /// <param name="payload">The string content to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><c>true</c> if the content is detected as malware; otherwise, <c>false</c>.</returns>
    /// <exception cref="Win32Exception">Thrown when the scan operation fails.</exception>
    public bool IsMalware(string payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanString(_context._handle, payload, contentName, _sessionHandle, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    /// <summary>Scans a byte buffer for malware within this session.</summary>
    /// <param name="payload">The byte buffer to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><c>true</c> if the content is detected as malware; otherwise, <c>false</c>.</returns>
    /// <exception cref="Win32Exception">Thrown when the scan operation fails.</exception>
    public bool IsMalware(byte[] payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanBuffer(_context._handle, payload, (uint)payload.Length, contentName, _sessionHandle, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    public void Dispose()
    {
        _sessionHandle.Dispose();
    }
}
