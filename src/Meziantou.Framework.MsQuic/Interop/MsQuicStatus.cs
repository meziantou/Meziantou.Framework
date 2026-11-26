namespace Meziantou.Framework.MsQuic.Interop;

// QUIC_STATUS: HRESULT values on Windows (msquic_winuser.h), errno values elsewhere (msquic_posix.h), some of which
// differ between Linux and macOS.
internal static class MsQuicStatus
{
    public const int Success = 0;

    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    private static readonly bool IsBsd = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD();

    public static readonly int Pending = IsWindows ? 0x703E5 : -2;

    public static readonly int OutOfMemory = IsWindows ? unchecked((int)0x8007000E) : 12;

    public static readonly int InvalidParameter = IsWindows ? unchecked((int)0x80070057) : 22;

    public static readonly int InvalidState = IsWindows ? unchecked((int)0x8007139F) : 1;

    public static readonly int NotSupported = IsWindows ? unchecked((int)0x80004002) : IsBsd ? 102 : 95;

    public static readonly int BufferTooSmall = IsWindows ? unchecked((int)0x8007007A) : IsBsd ? 84 : 75;

    public static readonly int HandshakeFailure = IsWindows ? unchecked((int)0x80410000) : IsBsd ? 53 : 103;

    public static readonly int Aborted = IsWindows ? unchecked((int)0x80004004) : IsBsd ? 89 : 125;

    public static readonly int AddressInUse = IsWindows ? unchecked((int)0x80072740) : IsBsd ? 48 : 98;

    public static readonly int InvalidAddress = IsWindows ? unchecked((int)0x80072741) : IsBsd ? 47 : 97;

    public static readonly int ConnectionTimeout = IsWindows ? unchecked((int)0x80410006) : IsBsd ? 60 : 110;

    public static readonly int ConnectionIdle = IsWindows ? unchecked((int)0x80410005) : IsBsd ? 101 : 62;

    public static readonly int InternalError = IsWindows ? unchecked((int)0x80410003) : 5;

    public static readonly int ConnectionRefused = IsWindows ? unchecked((int)0x800704C9) : IsBsd ? 61 : 111;

    public static readonly int ProtocolError = IsWindows ? unchecked((int)0x80410004) : IsBsd ? 100 : 71;

    public static readonly int VersionNegotiationError = IsWindows ? unchecked((int)0x80410001) : IsBsd ? 43 : 93;

    public static readonly int Unreachable = IsWindows ? unchecked((int)0x800704D0) : IsBsd ? 65 : 113;

    public static readonly int TlsError = IsWindows ? unchecked((int)0x80072B18) : 126;

    public static readonly int UserCanceled = IsWindows ? unchecked((int)0x80410002) : IsBsd ? 105 : 130;

    public static readonly int AlpnNegotiationFailure = IsWindows ? unchecked((int)0x80410007) : IsBsd ? 42 : 92;

    public static readonly int StreamLimitReached = IsWindows ? unchecked((int)0x80410008) : 86;

    public static readonly int AlpnInUse = IsWindows ? unchecked((int)0x80410009) : IsBsd ? 41 : 91;

    public static readonly int AddressNotAvailable = IsWindows ? unchecked((int)0x80072741) : IsBsd ? 49 : 99;

    private static readonly int TlsAlertBase = IsWindows ? unchecked((int)0x80410100) : 200_000_000 + 256;

    // CERT_E_* (0x800B01xx) and SEC_E_* (0x8009xxxx) on Windows
    private static readonly int CertificateErrorFirst = IsWindows ? unchecked((int)0x80090000) : 200_000_000 + 512;

    private static readonly int CertificateErrorLast = IsWindows ? unchecked((int)0x800BFFFF) : 200_000_000 + 767;

    public static readonly int BadCertificate = TlsAlert(42);

    public static bool IsFailure(int status) => OperatingSystem.IsWindows() ? status < 0 : status > 0;

    public static int TlsAlert(byte alert) => TlsAlertBase | alert;

    public static bool IsTlsAlert(int status) => (status & ~0xFF) == TlsAlertBase;

    public static bool IsCertificateError(int status) => status >= CertificateErrorFirst && status <= CertificateErrorLast;

    public static MsQuicError ToError(int status)
    {
        if (status == ConnectionIdle)
            return MsQuicError.ConnectionIdle;

        if (status == ConnectionTimeout)
            return MsQuicError.ConnectionTimeout;

        if (status == ConnectionRefused)
            return MsQuicError.ConnectionRefused;

        if (status == Unreachable)
            return MsQuicError.HostUnreachable;

        if (status == AddressInUse)
            return MsQuicError.AddressInUse;

        if (status == InvalidAddress || status == AddressNotAvailable)
            return MsQuicError.InvalidAddress;

        if (status == VersionNegotiationError)
            return MsQuicError.VersionNegotiationError;

        if (status == ProtocolError)
            return MsQuicError.ProtocolError;

        if (status == AlpnInUse)
            return MsQuicError.AlpnInUse;

        if (status == StreamLimitReached)
            return MsQuicError.StreamLimitReached;

        if (status == Aborted || status == UserCanceled)
            return MsQuicError.OperationAborted;

        if (status == HandshakeFailure || status == TlsError || status == AlpnNegotiationFailure || IsTlsAlert(status) || IsCertificateError(status))
            return MsQuicError.HandshakeFailure;

        return MsQuicError.InternalError;
    }
}
