using System.Security.Cryptography;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>
/// What an object was when it was loaded: the hash of its syntax and, for a stream, the version of its data. An
/// incremental save compares it with the object as it is to find the objects that changed.
/// </summary>
internal sealed class PdfObjectFingerprint
{
    private readonly byte[] _hash;
    private readonly int _dataVersion;

    private PdfObjectFingerprint(byte[] hash, int dataVersion)
    {
        _hash = hash;
        _dataVersion = dataVersion;
    }

    public static PdfObjectFingerprint Compute(PdfObject value)
    {
        var writer = new PdfByteWriter(256);
        try
        {
            PdfObjectSerializer.Write(writer, value, ReferenceWriter.Identity);
        }
        catch (InvalidOperationException)
        {
            // An object too deep to be written has no fingerprint: it is always considered changed
            return new PdfObjectFingerprint([], -1);
        }

        return new PdfObjectFingerprint(SHA256.HashData(writer.WrittenSpan), value is PdfStream stream ? stream.DataVersion : 0);
    }

    public bool Matches(PdfObject value)
    {
        if (_dataVersion < 0)
            return false;

        if (value is PdfStream stream && stream.DataVersion != _dataVersion)
            return false;

        var current = Compute(value);
        return current._hash.AsSpan().SequenceEqual(_hash);
    }
}
