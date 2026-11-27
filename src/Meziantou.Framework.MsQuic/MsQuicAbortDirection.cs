namespace Meziantou.Framework.MsQuic;

/// <summary>The directions of a stream that <see cref="MsQuicStream.Abort"/> aborts.</summary>
[Flags]
public enum MsQuicAbortDirection
{
    /// <summary>Aborts the reading side: sends STOP_SENDING to the peer.</summary>
    Read = 1,

    /// <summary>Aborts the writing side: sends RESET_STREAM to the peer.</summary>
    Write = 2,

    /// <summary>Aborts both sides.</summary>
    Both = Read | Write,
}
