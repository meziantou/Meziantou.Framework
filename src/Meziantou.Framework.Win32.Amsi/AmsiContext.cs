using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Meziantou.Framework.Win32;

/// <summary>Provides access to the Windows Antimalware Scan Interface (AMSI) for scanning content for malware.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Win32.AmsiTests/AmsiContextTests.cs" ref:region="AmsiContext_IsMalware" ref:dedent="true">
/// using var context = AmsiContext.Create("MyApplication");
///
/// // The EICAR test file is detected as malware by every antivirus
/// Assert.True(context.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "test.txt"));
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

    /// <summary>Determines whether at least one antimalware provider is registered with AMSI.</summary>
    /// <returns><see langword="true"/> when a provider is registered; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// When no provider is registered, scans succeed and report every content as <see cref="AmsiResult.NotDetected"/>:
    /// a negative answer does not mean that the content is safe. A registered provider may still be unable to scan,
    /// for instance when it is disabled; scans then throw a <see cref="COMException"/>.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Win32.AmsiTests/AmsiContextTests.cs" ref:region="AmsiContext_IsProviderRegistered" ref:dedent="true">
    /// // Without a provider, every content is reported as not detected
    /// if (!AmsiContext.IsProviderRegistered())
    ///     throw new InvalidOperationException("No antimalware product is registered with AMSI");
    ///
    /// foreach (var provider in AmsiContext.GetRegisteredProviders())
    /// {
    ///     // e.g. {2781761E-28E0-4109-99FE-B9D127C57AFE} Windows Defender IOfficeAntiVirus implementation
    ///     Assert.NotEqual(Guid.Empty, provider.Id);
    /// }
    /// </code>
    /// </example>
    public static bool IsProviderRegistered()
    {
        return GetRegisteredProviders().Count > 0;
    }

    /// <summary>Gets the antimalware providers registered with AMSI, in the order AMSI loads them.</summary>
    /// <returns>The registered providers, or an empty list when no provider is registered.</returns>
    /// <remarks>
    /// AMSI reads its providers from the <c>HKLM\SOFTWARE\Microsoft\AMSI\Providers</c> registry key. The registry view of
    /// the current process is used, as AMSI does: a 32-bit process sees the providers registered for 32-bit processes.
    /// </remarks>
    public static IReadOnlyList<AmsiProvider> GetRegisteredProviders()
    {
        return GetRegisteredProviders(Registry.LocalMachine);
    }

    /// <summary>Reads the providers from the AMSI and COM registration keys under <paramref name="root"/>.</summary>
    internal static IReadOnlyList<AmsiProvider> GetRegisteredProviders(RegistryKey root)
    {
        using var providersKey = root.OpenSubKey(@"SOFTWARE\Microsoft\AMSI\Providers");
        if (providersKey is null)
            return [];

        var result = new List<AmsiProvider>();
        foreach (var name in providersKey.GetSubKeyNames())
        {
            if (!Guid.TryParse(name, out var id))
                continue;

            using var classKey = root.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + id.ToString("B"));
            using var serverKey = classKey?.OpenSubKey("InprocServer32");
            var displayName = classKey?.GetValue(name: null) as string;
            var modulePath = serverKey?.GetValue(name: null) as string;
            result.Add(new AmsiProvider(id, NullIfEmpty(displayName), NullIfEmpty(modulePath?.Trim('"'))));
        }

        return result;

        static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
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
