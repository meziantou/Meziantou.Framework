using System.Collections.Concurrent;
using System.Text;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>The state shared by the objects of a file.</summary>
internal sealed class PstContext
{
    private static readonly ConcurrentDictionary<int, Encoding> Encodings = new();
    private NameToIdMap? _nameToIdMap;

    public PstContext(NdbReader reader)
    {
        Reader = reader;
    }

    public NdbReader Reader { get; }

    public PstReadOptions Options => Reader.Options;

    public NameToIdMap NameToIdMap => _nameToIdMap ??= NameToIdMap.Read(Reader);

    /// <summary>The code page of the 8-bit strings of the objects that do not specify one.</summary>
    public Encoding DefaultEncoding { get; set; } = GetEncoding(1252);

    public static Encoding GetEncoding(int codePage)
    {
        return Encodings.GetOrAdd(codePage, value =>
        {
            if (value is 1200)
            {
                return Encoding.Unicode;
            }

            if (value is 65001)
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            try
            {
                return CodePagesEncodingProvider.Instance.GetEncoding(value) ?? Encoding.GetEncoding(value);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;
            }
        });
    }
}
