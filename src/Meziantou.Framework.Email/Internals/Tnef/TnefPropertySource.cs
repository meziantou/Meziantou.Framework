using System.Text;
using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Internals.Tnef;

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

    public static MapiPropertyCollection Create(TnefContext context, List<TnefProperty> properties, Encoding defaultEncoding)
    {
        var raw = new List<RawProperty>(properties.Count);
        var tags = new HashSet<MapiPropertyTag>();
        foreach (var property in properties)
        {
            if (tags.Add(property.Tag))
            {
                raw.Add(RawProperty.FromInline(property.Tag, property.Value));
            }
        }

        return new MapiPropertyCollection(new TnefPropertySource(context, defaultEncoding), raw);
    }

    public override MapiNamedProperty? GetName(ushort id) => _context.GetName(id);

    public override ushort? GetId(MapiNamedProperty name) => _context.GetId(name);

    public override byte[] ReadBytes(RawProperty property) => property.Inline ?? [];

    public override long GetSize(RawProperty property) => property.Inline?.Length ?? 0;
}
