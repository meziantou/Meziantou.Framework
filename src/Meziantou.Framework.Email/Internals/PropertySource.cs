using System.Text;
using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Where the values of properties are, and how the identifiers of the named properties map to their names.</summary>
internal abstract class PropertySource
{
    /// <summary>Gets the encoding of the 8-bit strings when the object does not give its code page.</summary>
    public abstract Encoding DefaultEncoding { get; }

    public abstract MapiNamedProperty? GetName(ushort id);

    public abstract ushort? GetId(MapiNamedProperty name);

    public abstract byte[] ReadBytes(RawProperty property);

    public abstract long GetSize(RawProperty property);

    public virtual Stream OpenRead(RawProperty property) => new MemoryStream(ReadBytes(property), writable: false);

    public object? Decode(RawProperty property, Encoding string8Encoding)
    {
        if (property.Tag.Type == MapiPropertyType.ObjectReference)
        {
            // An object value is a reference to the object (a subnode, a storage), not decoded here
            return ReadBytes(property);
        }

        return PropertyValueCodec.Decode(property.Tag.Type, ReadBytes(property), string8Encoding);
    }
}
