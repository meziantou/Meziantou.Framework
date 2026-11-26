namespace Meziantou.Framework.MsQuic.Interop;

internal static class QuicFlags
{
    public const int CredentialTypeNone = 0;
    public const int CredentialTypeCertificateContext = 3;
    public const int CredentialTypeCertificatePkcs12 = 6;

    public const int CredentialClient = 0x1;
    public const int CredentialNoCertificateValidation = 0x4;
    public const int CredentialIndicateCertificateReceived = 0x10;
    public const int CredentialUsePortableCertificates = 0x4000;

    public const int TlsProviderSchannel = 0;

    public const int ConnectionShutdownNone = 0;

    public const int StreamOpenUnidirectional = 0x1;
    public const int StreamStartImmediate = 0x1;
    public const int StreamStartFailBlocked = 0x2;
    public const int StreamStartShutdownOnFail = 0x4;
    public const int StreamStartIndicatePeerAccept = 0x8;
    public const int StreamShutdownGraceful = 0x1;
    public const int StreamShutdownAbortSend = 0x2;
    public const int StreamShutdownAbortReceive = 0x4;
    public const int StreamShutdownImmediate = 0x8;

    public const int SendFin = 0x4;

    public const int DatagramSendUnknown = 0;
    public const int DatagramSendSent = 1;
    public const int DatagramSendLostSuspect = 2;
    public const int DatagramSendLostDiscarded = 3;
    public const int DatagramSendAcknowledged = 4;
    public const int DatagramSendAcknowledgedSpurious = 5;
    public const int DatagramSendCanceled = 6;
}
