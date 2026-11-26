namespace Meziantou.Framework.MsQuic.Interop;

internal static class QuicParameters
{
    public const uint GlobalLibraryVersion = 0x01000004;
    public const uint GlobalTlsProvider = 0x0100000A;
    public const uint ListenerLocalAddress = 0x04000000;
    public const uint ConnectionLocalAddress = 0x05000001;
    public const uint ConnectionRemoteAddress = 0x05000002;
    public const uint ConnectionStatisticsV2 = 0x05000016;
    public const uint StreamId = 0x08000000;
    public const uint StreamPriority = 0x08000003;
}
