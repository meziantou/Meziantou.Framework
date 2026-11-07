using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// The names a reader parsed, so that a name that occurs many times in a file (<c>/F1</c>, <c>/Im0</c>) is one instance.
/// The cache is bounded: the short names only, and a maximum number of them, so that a file cannot make it grow without
/// bound. Names are immutable, so sharing an instance changes nothing but the memory.
/// </summary>
internal sealed class PdfNameCache
{
    private const int MaxNameLength = 32;
    private const int MaxCount = 4096;

    private readonly Dictionary<string, PdfName> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PdfName>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public PdfNameCache()
    {
        _lookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public int Count => _names.Count;

    /// <summary>Gets the name of a name token.</summary>
    public PdfName Get(PdfLexer lexer, PdfToken token)
    {
        var length = PdfLexer.GetNameLength(token);
        if (length > MaxNameLength)
            return PdfName.FromBytes(lexer.DecodeName(token));

        Span<byte> bytes = stackalloc byte[MaxNameLength];
        bytes = bytes[..lexer.DecodeName(token, bytes)];

        // The key of a name is its bytes as ISO-8859-1 characters
        Span<char> key = stackalloc char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            key[i] = (char)bytes[i];
        }

        if (PdfNames.TryGet(key, out var known))
            return known;

        if (_lookup.TryGetValue(key, out var cached))
            return cached;

        var name = PdfName.FromBytes(bytes);
        if (_names.Count < MaxCount)
        {
            _names.Add(name.Key, name);
        }

        return name;
    }
}
