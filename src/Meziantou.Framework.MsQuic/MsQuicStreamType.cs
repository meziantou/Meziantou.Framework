namespace Meziantou.Framework.MsQuic;

/// <summary>The type of a QUIC stream.</summary>
public enum MsQuicStreamType
{
    /// <summary>A stream that only the endpoint that opens it can write to.</summary>
    Unidirectional,

    /// <summary>A stream that both endpoints can read from and write to.</summary>
    Bidirectional,
}
