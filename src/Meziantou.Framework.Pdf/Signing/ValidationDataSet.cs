namespace Meziantou.Framework.Pdf.Signing;

/// <summary>The certificates and the revocation evidence that validate one signature: the content of its VRI dictionary (ISO 32000-2, 12.8.4.4).</summary>
internal sealed class ValidationDataSet
{
    public List<byte[]> Certificates { get; } = [];

    public List<byte[]> CertificateRevocationLists { get; } = [];

    public List<byte[]> OcspResponses { get; } = [];

    public void AddCertificate(byte[] data) => Add(Certificates, data);

    public void AddCertificateRevocationList(byte[] data) => Add(CertificateRevocationLists, data);

    public void AddOcspResponse(byte[] data) => Add(OcspResponses, data);

    private static void Add(List<byte[]> list, byte[] data)
    {
        if (!list.Exists(item => item.AsSpan().SequenceEqual(data)))
        {
            list.Add(data);
        }
    }
}
