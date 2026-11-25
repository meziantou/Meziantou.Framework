using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 56)]
internal struct QuicCredentialConfig
{
    [FieldOffset(0)]
    public safe int Type;

    [FieldOffset(4)]
    public safe int Flags;

    // A pointer whose type depends on Type: CERT_CONTEXT* (CertificateContext), QUIC_CERTIFICATE_PKCS12*...
    [FieldOffset(8)]
    public safe void* Certificate;

    [FieldOffset(16)]
    public safe byte* Principal;

    [FieldOffset(40)]
    public safe int AllowedCipherSuites;
}
