using System.ComponentModel;
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
[SupportedOSPlatform("windows")]
public sealed class AmsiContext : IDisposable
{
    internal readonly AmsiContextSafeHandle _handle;

    private static readonly AmsiSessionSafeHandle DefaultSession = new();

    private AmsiContext(AmsiContextSafeHandle context)
    {
        _handle = context;
    }

    /// <summary>Creates a new AMSI context for the specified application.</summary>
    /// <param name="applicationName">The name of the application that will use the AMSI context.</param>
    /// <returns>A new <see cref="AmsiContext"/> instance.</returns>
    /// <exception cref="Win32Exception">Thrown when the AMSI context cannot be initialized.</exception>
    public static AmsiContext Create(string applicationName)
    {
        var result = Amsi.AmsiInitialize(applicationName, out var context);
        if (result != 0)
            throw new Win32Exception(result);

        return new AmsiContext(context);
    }

    /// <summary>Creates a new AMSI session for correlating multiple scan requests.</summary>
    /// <returns>A new <see cref="AmsiSession"/> instance.</returns>
    /// <exception cref="Win32Exception">Thrown when the AMSI session cannot be opened.</exception>
    public AmsiSession CreateSession()
    {
        var result = Amsi.AmsiOpenSession(_handle, out var session);
        session.Context = _handle;
        if (result != 0)
            throw new Win32Exception(result);

        return new AmsiSession(this, session);
    }

    /// <summary>Scans a string for malware using the Windows antimalware provider.</summary>
    /// <param name="payload">The string content to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><see langword="true"/> if the content is detected as malware; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="Win32Exception">Thrown when the scan operation fails.</exception>
    public bool IsMalware(string payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanString(_handle, payload, contentName, DefaultSession, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    /// <summary>Scans a byte buffer for malware using the Windows antimalware provider.</summary>
    /// <param name="payload">The byte buffer to scan.</param>
    /// <param name="contentName">The name or identifier of the content being scanned.</param>
    /// <returns><see langword="true"/> if the content is detected as malware; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="Win32Exception">Thrown when the scan operation fails.</exception>
    public bool IsMalware(byte[] payload, string contentName)
    {
        var returnValue = Amsi.AmsiScanBuffer(_handle, payload, (uint)payload.Length, contentName, DefaultSession, out var result);
        if (returnValue != 0)
            throw new Win32Exception(returnValue);

        return Amsi.AmsiResultIsMalware(result);
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}
