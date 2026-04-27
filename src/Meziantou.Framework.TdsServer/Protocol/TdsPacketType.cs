namespace Meziantou.Framework.Tds.Protocol;

internal enum TdsPacketType : byte
{
    SqlBatch = 0x01,
    Rpc = 0x03,
    TabularResult = 0x04,
    Attention = 0x06,
    Login7 = 0x10,
    Sspi = 0x11,
    PreLogin = 0x12,
}
