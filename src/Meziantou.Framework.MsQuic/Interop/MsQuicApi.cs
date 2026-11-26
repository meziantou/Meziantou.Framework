using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.MsQuic.Interop;

// Loads MsQuic and opens its API table (MsQuicOpenVersion) and the registration of the process. Both stay open for
// the lifetime of the process, as System.Net.Quic does.
internal sealed class MsQuicApi
{
    private const uint ApiVersion = 2;

    private static readonly Lazy<MsQuicApi?> LazyInstance = new(Create);
    private static string? s_notSupportedReason;

    private MsQuicApi(QuicApiTable* table, nint registration, Version version, bool usesSchannel)
    {
        Table = table;
        Registration = registration;
        Version = version;
        UsesSchannel = usesSchannel;
    }

    public QuicApiTable* Table { get; }

    public nint Registration { get; }

    public Version Version { get; }

    public bool UsesSchannel { get; }

    public static MsQuicApi? Instance => LazyInstance.Value;

    public static string? NotSupportedReason
    {
        get
        {
            _ = LazyInstance.Value;
            return s_notSupportedReason;
        }
    }

    public static MsQuicApi Get()
    {
        var instance = Instance;
        if (instance is null)
            throw new PlatformNotSupportedException("MsQuic is not available: " + NotSupportedReason);

        return instance;
    }

    public static void ThrowIfFailure(int status, string operation)
    {
        if (MsQuicStatus.IsFailure(status))
        {
            throw new MsQuicException(MsQuicStatus.ToError(status), applicationErrorCode: null, transportErrorCode: null, $"{operation} failed with status 0x{status:X8}.") { NativeErrorCode = status };
        }
    }

    private static MsQuicApi? Create()
    {
        if (OperatingSystem.IsBrowser())
        {
            s_notSupportedReason = "MsQuic does not run in the browser.";
            return null;
        }

        if (!Environment.Is64BitProcess)
        {
            s_notSupportedReason = "MsQuic is only supported in 64-bit processes.";
            return null;
        }

        if (!TryLoadLibrary(out var library))
        {
            s_notSupportedReason = OperatingSystem.IsWindows()
                ? "msquic.dll was not found."
                : "libmsquic was not found. Install the libmsquic package (https://learn.microsoft.com/dotnet/fundamentals/networking/quic/quic-overview#platform-dependencies).";
            return null;
        }

        if (!NativeLibrary.TryGetExport(library, "MsQuicOpenVersion", out var openExport))
        {
            s_notSupportedReason = "The MsQuic library does not export MsQuicOpenVersion.";
            return null;
        }

        unsafe
        {
            var open = (delegate* unmanaged[Cdecl]<uint, QuicApiTable**, int>)openExport;
            QuicApiTable* table;
            var status = open(ApiVersion, &table);
            if (MsQuicStatus.IsFailure(status))
            {
                s_notSupportedReason = $"MsQuicOpenVersion({ApiVersion}) failed with status 0x{status:X8}.";
                return null;
            }

            var versionParts = stackalloc uint[4];
            var length = (uint)(sizeof(uint) * 4);
            var version = MsQuicStatus.IsFailure(table->GetParam(0, QuicParameters.GlobalLibraryVersion, &length, versionParts))
                ? new Version(0, 0)
                : new Version((int)versionParts[0], (int)versionParts[1], (int)versionParts[2]);

            int tlsProvider;
            length = sizeof(int);
            var usesSchannel = !MsQuicStatus.IsFailure(table->GetParam(0, QuicParameters.GlobalTlsProvider, &length, &tlsProvider)) && tlsProvider == QuicFlags.TlsProviderSchannel;

            var appName = "Meziantou.Framework.MsQuic\0"u8;
            nint registration;
            fixed (byte* appNamePointer = appName)
            {
                var registrationConfig = new QuicRegistrationConfig { AppName = appNamePointer, ExecutionProfile = 0 }; // QUIC_EXECUTION_PROFILE_LOW_LATENCY
                status = table->RegistrationOpen(&registrationConfig, &registration);
            }

            if (MsQuicStatus.IsFailure(status))
            {
                s_notSupportedReason = $"RegistrationOpen failed with status 0x{status:X8}.";
                return null;
            }

            return new MsQuicApi(table, registration, version, usesSchannel);
        }
    }

    private static bool TryLoadLibrary(out nint library)
    {
        if (OperatingSystem.IsWindows())
        {
            // .NET ships msquic.dll in the shared framework, next to System.Net.Quic.dll. A self-contained application
            // has it in its own folder.
            return NativeLibrary.TryLoad(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "msquic.dll"), out library)
                || NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "msquic.dll"), out library)
                || NativeLibrary.TryLoad("msquic.dll", typeof(MsQuicApi).Assembly, searchPath: null, out library);
        }

        string[] names = OperatingSystem.IsMacOS()
            ? ["libmsquic.2.dylib", "libmsquic.dylib", "/opt/homebrew/lib/libmsquic.2.dylib", "/opt/homebrew/lib/libmsquic.dylib", "/usr/local/lib/libmsquic.2.dylib", "/usr/local/lib/libmsquic.dylib"]
            : ["libmsquic.so.2", "libmsquic.so"];
        foreach (var name in names)
        {
            if (NativeLibrary.TryLoad(name, typeof(MsQuicApi).Assembly, searchPath: null, out library))
                return true;
        }

        library = 0;
        return false;
    }

    public static string GetAsciiString(byte* value, int length)
    {
        if (length == 0)
            return "";

        unsafe
        {
            return Encoding.ASCII.GetString(value, length);
        }
    }
}
