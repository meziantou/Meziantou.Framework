using System;

namespace Meziantou.Framework.Tds.Protocol;

[Flags]
internal enum TdsPacketStatus : byte
{
    None = 0x00,
    EndOfMessage = 0x01,
}
