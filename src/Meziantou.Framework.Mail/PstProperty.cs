using System.Text;
using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail;

/// <summary>A MAPI property of an object of an Outlook data file. The value is read when it is first accessed.</summary>
public sealed class PstProperty
{
    private readonly PropertySource _source;
    private readonly RawProperty _raw;
    private readonly Encoding _encoding;
    private object? _value;
    private bool _valueRead;

    internal PstProperty(PropertySource source, RawProperty raw, Encoding encoding)
    {
        _source = source;
        _raw = raw;
        _encoding = encoding;
        if (raw.Tag.IsNamed)
        {
            NamedProperty = source.Context.NameToIdMap.GetName(raw.Tag.Id);
        }
    }

    internal RawProperty Raw => _raw;

    internal PropertySource Source => _source;

    /// <summary>Gets the tag of the property.</summary>
    public PstPropertyTag Tag => _raw.Tag;

    /// <summary>Gets the identifier of the property.</summary>
    public ushort Id => _raw.Tag.Id;

    /// <summary>Gets the type of the property.</summary>
    public PstPropertyType Type => _raw.Tag.Type;

    /// <summary>Gets the name of the property when its identifier is in the range of the named properties (0x8000 and above) and the file maps it.</summary>
    public PstNamedProperty? NamedProperty { get; }

    /// <summary>Gets the value of the property, as the .NET type documented by <see cref="PstPropertyType"/>, or the raw bytes for the types that have no .NET equivalent.</summary>
    /// <exception cref="InvalidPstException">The value is malformed.</exception>
    /// <exception cref="PstResourceLimitException">The value is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public object? Value
    {
        get
        {
            if (!_valueRead)
            {
                _value = _source.Decode(_raw, _encoding);
                _valueRead = true;
            }

            return _value;
        }
    }

    /// <summary>Gets the size of the value in bytes, as stored in the file.</summary>
    /// <returns>The size of the value.</returns>
    public long GetSize() => _source.GetSize(_raw);

    /// <summary>Returns the bytes of the value, as stored in the file (UTF-16 for <see cref="PstPropertyType.Unicode"/>).</summary>
    /// <returns>The bytes of the value.</returns>
    /// <exception cref="PstResourceLimitException">The value is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public byte[] GetBytes() => _source.ReadBytes(_raw);

    /// <summary>Opens a stream on the bytes of the value. Large values are read block by block.</summary>
    /// <returns>A read-only stream.</returns>
    public Stream OpenRead() => _source.OpenRead(_raw);

    /// <summary>Returns the tag and the name of the property.</summary>
    /// <returns>The tag and the name of the property.</returns>
    public override string ToString() => NamedProperty is null ? Tag.ToString() : Tag + " " + NamedProperty;
}
