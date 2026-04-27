namespace Meziantou.Framework.Tds.Protocol;

internal sealed class TdsPacket
{
    public required TdsPacketType Type { get; init; }

    public required byte[] Payload { get; init; }
}
