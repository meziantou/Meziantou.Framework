using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct QuicCertificatePkcs12
{
    [FieldOffset(0)]
    public safe byte* Asn1Blob;

    [FieldOffset(8)]
    public safe uint Asn1BlobLength;

    [FieldOffset(16)]
    public safe byte* PrivateKeyPassword;
}
