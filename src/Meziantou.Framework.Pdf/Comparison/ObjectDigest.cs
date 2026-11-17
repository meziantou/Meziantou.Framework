using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A digest of an object and of the objects it refers to, independent of their object numbers.</summary>
internal static class ObjectDigest
{
    private const int MaxDepth = 32;
    private const int MaxObjects = 10_000;

    public static string Compute(PdfObject value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        var count = 0;
        Append(hash, value, depth: 0, visited, ref count);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, PdfObject value, int depth, HashSet<PdfObject> visited, ref int count)
    {
        if (depth > MaxDepth || ++count > MaxObjects)
        {
            hash.AppendData("!"u8);
            return;
        }

        if (value is PdfReference reference)
        {
            try
            {
                value = reference.Resolve();
            }
            catch (PdfException)
            {
                value = PdfNull.Instance;
            }
        }

        switch (value)
        {
            case PdfDictionary or PdfArray or PdfStream when !visited.Add(value):
                // A cycle, or an object already hashed: its content is not hashed again
                hash.AppendData("@"u8);
                break;

            case PdfDictionary dictionary:
                AppendDictionary(hash, dictionary, depth, visited, ref count);
                break;

            case PdfStream stream:
                hash.AppendData("S"u8);
                AppendDictionary(hash, stream.Dictionary, depth, visited, ref count);
                try
                {
                    hash.AppendData(stream.EncodedData.Span);
                }
                catch (PdfException)
                {
                }

                break;

            case PdfArray array:
                hash.AppendData("["u8);
                foreach (var item in array)
                {
                    Append(hash, item, depth + 1, visited, ref count);
                }

                hash.AppendData("]"u8);
                break;

            case PdfString text:
                hash.AppendData("("u8);
                hash.AppendData(text.Bytes.Span);
                hash.AppendData(")"u8);
                break;

            default:
                hash.AppendData(Encoding.UTF8.GetBytes(value.ToString() + " "));
                break;
        }
    }

    private static void AppendDictionary(IncrementalHash hash, PdfDictionary dictionary, int depth, HashSet<PdfObject> visited, ref int count)
    {
        hash.AppendData("<"u8);
        // In the order of the keys: the order a writer chose does not change the object
        foreach (var (key, item) in dictionary.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal))
        {
            // The length depends on the filters a writer chose, not on the object
            if (key == PdfNames.Length)
                continue;

            hash.AppendData(Encoding.UTF8.GetBytes("/" + key.Value + " "));
            Append(hash, item, depth + 1, visited, ref count);
        }

        hash.AppendData(">"u8);
    }
}
