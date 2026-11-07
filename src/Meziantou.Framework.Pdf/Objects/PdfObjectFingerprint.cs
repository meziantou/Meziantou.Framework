namespace Meziantou.Framework.Pdf.Objects;

/// <summary>
/// What an object was when it was loaded: the instance, and for a stream the version of its data. An incremental save
/// compares it with the object as it is to find the objects that changed.
/// </summary>
/// <remarks>
/// Taking the fingerprint costs nothing: the containers (<see cref="PdfDictionary"/>, <see cref="PdfArray"/>) record that
/// they were modified after they were parsed, and the save walks the direct objects of the loaded objects only. An object
/// is changed when it was replaced, when one of its containers was modified, or when the data of its stream changed.
/// </remarks>
internal sealed class PdfObjectFingerprint
{
    private const int MaxDepth = 256;

    private readonly PdfObject _value;
    private readonly int _dataVersion;

    private PdfObjectFingerprint(PdfObject value, int dataVersion)
    {
        _value = value;
        _dataVersion = dataVersion;
    }

    public static PdfObjectFingerprint Compute(PdfObject value) => new(value, value is PdfStream stream ? stream.DataVersion : 0);

    public bool Matches(PdfObject value)
    {
        if (!ReferenceEquals(value, _value))
            return false;

        if (value is PdfStream stream && stream.DataVersion != _dataVersion)
            return false;

        return !IsModified(value, depth: 0);
    }

    private static bool IsModified(PdfObject value, int depth)
    {
        // An object too deep to be walked is always considered changed
        if (depth > MaxDepth)
            return true;

        switch (value)
        {
            case PdfDictionary dictionary:
                if (dictionary.IsModified)
                    return true;

                foreach (var (_, item) in dictionary)
                {
                    if (item is PdfDictionary or PdfArray && IsModified(item, depth + 1))
                        return true;
                }

                return false;

            case PdfArray array:
                if (array.IsModified)
                    return true;

                foreach (var item in array)
                {
                    if (item is PdfDictionary or PdfArray && IsModified(item, depth + 1))
                        return true;
                }

                return false;

            case PdfStream stream:
                return IsModified(stream.Dictionary, depth + 1);

            default:
                return false;
        }
    }
}
