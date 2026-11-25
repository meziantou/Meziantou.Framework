using Meziantou.Framework.MsQuic.Interop;

namespace Meziantou.Framework.MsQuic;

/// <summary>Describes the MsQuic library the process loaded.</summary>
/// <remarks>
/// On Windows, MsQuic ships with .NET. On Linux, install the <c>libmsquic</c> package; on macOS, run
/// <c>brew install libmsquic</c>. Only 64-bit processes are supported.
/// </remarks>
public static class MsQuicLibrary
{
    /// <summary>Gets a value indicating whether MsQuic could be loaded.</summary>
    public static bool IsSupported => MsQuicApi.Instance is not null;

    /// <summary>Gets why MsQuic could not be loaded, or <see langword="null"/> when it is supported.</summary>
    public static string? NotSupportedReason => MsQuicApi.NotSupportedReason;

    /// <summary>Gets the version of the loaded MsQuic library, or <see langword="null"/> when it is not supported.</summary>
    public static Version? Version => MsQuicApi.Instance?.Version;

    /// <summary>Gets a value indicating whether MsQuic uses Schannel (Windows) instead of OpenSSL for TLS.</summary>
    public static bool UsesSchannel => MsQuicApi.Instance?.UsesSchannel ?? false;
}
