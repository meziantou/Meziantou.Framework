using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

// The declarations of msquic.h (API version 2) are declared with their 64-bit offsets: the library only loads MsQuic in
// 64-bit processes (see MsQuicApi), where the layouts of Windows (LLP64), Linux and macOS (LP64) are the same, since
// msquic.h declares no long field. QUIC_ADDR is the exception: it is the native socket address, see MsQuicAddress.
// QUIC_API_TABLE, in the order of msquic.h. QUIC_API is __cdecl on Windows and the default convention elsewhere.
[StructLayout(LayoutKind.Sequential)]
internal struct QuicApiTable
{
    public delegate* unmanaged[Cdecl]<nint, void*, void> SetContext;
    public delegate* unmanaged[Cdecl]<nint, void*> GetContext;
    public delegate* unmanaged[Cdecl]<nint, void*, void*, void> SetCallbackHandler;
    public delegate* unmanaged[Cdecl]<nint, uint, uint, void*, int> SetParam;
    public delegate* unmanaged[Cdecl]<nint, uint, uint*, void*, int> GetParam;
    public delegate* unmanaged[Cdecl]<QuicRegistrationConfig*, nint*, int> RegistrationOpen;
    public delegate* unmanaged[Cdecl]<nint, void> RegistrationClose;
    public delegate* unmanaged[Cdecl]<nint, int, ulong, void> RegistrationShutdown;
    public delegate* unmanaged[Cdecl]<nint, QuicBuffer*, uint, QuicSettings*, uint, void*, nint*, int> ConfigurationOpen;
    public delegate* unmanaged[Cdecl]<nint, void> ConfigurationClose;
    public delegate* unmanaged[Cdecl]<nint, QuicCredentialConfig*, int> ConfigurationLoadCredential;
    public delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, void*, QuicListenerEvent*, int>, void*, nint*, int> ListenerOpen;
    public delegate* unmanaged[Cdecl]<nint, void> ListenerClose;
    public delegate* unmanaged[Cdecl]<nint, QuicBuffer*, uint, byte*, int> ListenerStart;
    public delegate* unmanaged[Cdecl]<nint, void> ListenerStop;
    public delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, void*, QuicConnectionEvent*, int>, void*, nint*, int> ConnectionOpen;
    public delegate* unmanaged[Cdecl]<nint, void> ConnectionClose;
    public delegate* unmanaged[Cdecl]<nint, int, ulong, void> ConnectionShutdown;
    public delegate* unmanaged[Cdecl]<nint, nint, ushort, byte*, ushort, int> ConnectionStart;
    public delegate* unmanaged[Cdecl]<nint, nint, int> ConnectionSetConfiguration;
    public void* ConnectionSendResumptionTicket;
    public delegate* unmanaged[Cdecl]<nint, int, delegate* unmanaged[Cdecl]<nint, void*, QuicStreamEvent*, int>, void*, nint*, int> StreamOpen;
    public delegate* unmanaged[Cdecl]<nint, void> StreamClose;
    public delegate* unmanaged[Cdecl]<nint, int, int> StreamStart;
    public delegate* unmanaged[Cdecl]<nint, int, ulong, int> StreamShutdown;
    public delegate* unmanaged[Cdecl]<nint, QuicBuffer*, uint, int, void*, int> StreamSend;
    public delegate* unmanaged[Cdecl]<nint, ulong, void> StreamReceiveComplete;
    public delegate* unmanaged[Cdecl]<nint, byte, int> StreamReceiveSetEnabled;
    public delegate* unmanaged[Cdecl]<nint, QuicBuffer*, uint, int, void*, int> DatagramSend;
}
