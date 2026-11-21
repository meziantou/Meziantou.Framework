using System.Text;
using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail.Internals.Tnef;

/// <summary>The properties of an object of a TNEF stream, read in memory.</summary>
internal sealed class TnefPropertySource : PropertySource
{
    private readonly TnefContext _context;

    private TnefPropertySource(TnefContext context, Encoding defaultEncoding)
    {
        _context = context;
        DefaultEncoding = defaultEncoding;
    }

    public override Encoding DefaultEncoding { get; }

    public static PstPropertyCollection Create(TnefContext context, List<TnefProperty> properties, Encoding defaultEncoding)
    {
        var raw = new List<RawProperty>(properties.Count);
        var tags = new HashSet<PstPropertyTag>();
        foreach (var property in properties)
        {
            if (tags.Add(property.Tag))
            {
                raw.Add(RawProperty.FromInline(property.Tag, property.Value));
            }
        }

        return new PstPropertyCollection(new TnefPropertySource(context, defaultEncoding), raw);
    }

    public override PstNamedProperty? GetName(ushort id) => _context.GetName(id);

    public override ushort? GetId(PstNamedProperty name) => _context.GetId(name);

    public override byte[] ReadBytes(RawProperty property) => property.Inline ?? [];

    public override long GetSize(RawProperty property) => property.Inline?.Length ?? 0;
}
