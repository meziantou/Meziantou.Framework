namespace Meziantou.Framework.Tds.Protocol;

internal enum TdsPreLoginEncryptionMode : byte
{
    Off = 0x00,
    On = 0x01,
    NotSupported = 0x02,
    Required = 0x03,
}
