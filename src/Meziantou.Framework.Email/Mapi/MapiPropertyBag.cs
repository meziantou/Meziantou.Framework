using System.Text;
using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>The MAPI properties of an object to write: tagged properties, and named properties the writer maps to identifiers.</summary>
/// <remarks>
/// The values are .NET values of the type documented by <see cref="MapiPropertyType"/> (<see cref="string"/>, <see cref="int"/>,
/// <see cref="DateTime"/>, <see cref="byte"/> arrays, arrays for the multi-valued types...). Strings are written as
/// <see cref="MapiPropertyType.String8"/> in an ANSI file.
/// </remarks>
public sealed class MapiPropertyBag
{
    private readonly Dictionary<ushort, Entry> _tagged = [];
    private readonly Dictionary<MapiNamedProperty, Entry> _named = [];

    /// <summary>Gets the number of properties.</summary>
    public int Count => _tagged.Count + _named.Count;

    /// <summary>Gets the tags of the tagged properties.</summary>
    public IEnumerable<MapiPropertyTag> Tags => _tagged.Values.Select(entry => new MapiPropertyTag(entry.Id, entry.Type));

    /// <summary>Gets the names of the named properties.</summary>
    public IEnumerable<MapiNamedProperty> Names => _named.Keys;

    /// <summary>Sets a tagged property, replacing the property that has the same identifier.</summary>
    /// <param name="tag">The tag of the property. Its identifier must be below 0x8000.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the property.</param>
    /// <exception cref="ArgumentException">The identifier is in the range of the named properties.</exception>
    public void Set(MapiPropertyTag tag, object? value)
    {
        if (tag.IsNamed)
        {
            throw new ArgumentException("Use the overload that takes a MapiNamedProperty for a named property.", nameof(tag));
        }

        if (value is null)
        {
            _tagged.Remove(tag.Id);
            return;
        }

        _tagged[tag.Id] = new Entry(tag.Id, tag.Type, value);
    }

    /// <summary>Sets a named property.</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="type">The type of the property.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the property.</param>
    public void Set(MapiNamedProperty name, MapiPropertyType type, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (value is null)
        {
            _named.Remove(name);
            return;
        }

        _named[name] = new Entry(0, type, value);
    }

    /// <summary>Returns the value of a tagged property.</summary>
    /// <param name="id">The identifier of the property.</param>
    /// <returns>The value, or <see langword="null"/> when the property is not set.</returns>
    public object? Get(ushort id) => _tagged.TryGetValue(id, out var entry) ? GetEntryValue(entry) : null;

    /// <summary>Returns the value of a named property.</summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value, or <see langword="null"/> when the property is not set.</returns>
    public object? Get(MapiNamedProperty name) => _named.TryGetValue(name, out var entry) ? GetEntryValue(entry) : null;

    /// <summary>Removes a tagged property.</summary>
    /// <param name="id">The identifier of the property.</param>
    /// <returns><see langword="true"/> when the property was set.</returns>
    public bool Remove(ushort id) => _tagged.Remove(id);

    /// <summary>Removes a named property.</summary>
    /// <param name="name">The name of the property.</param>
    /// <returns><see langword="true"/> when the property was set.</returns>
    public bool Remove(MapiNamedProperty name) => _named.Remove(name);

    internal T? GetValue<T>(ushort id) => Get(id) is T value ? value : default;

    internal T? GetValue<T>(NamedPropertyDefinition definition) => Get(definition.Name) is T value ? value : default;

    internal void SetValue(ushort id, MapiPropertyType type, object? value) => Set(new MapiPropertyTag(id, type), value);

    internal void SetValue(NamedPropertyDefinition definition, object? value) => Set(definition.Name, definition.Type, value);

    /// <summary>Sets a property to bytes copied from another file, written as they are.</summary>
    internal void SetRaw(MapiPropertyTag tag, MapiNamedProperty? name, byte[] bytes)
    {
        var entry = new Entry(tag.Id, tag.Type, new RawValue(bytes));
        if (name is null)
        {
            _tagged[tag.Id] = entry;
        }
        else
        {
            _named[name] = entry;
        }
    }

    /// <summary>Sets a property to a value the writer reads from a new stream each time it writes the object.</summary>
    internal void SetStream(MapiPropertyTag tag, MapiNamedProperty? name, Func<Stream> openStream)
    {
        var entry = new Entry(tag.Id, tag.Type, openStream);
        if (name is null)
        {
            _tagged[tag.Id] = entry;
        }
        else
        {
            _named[name] = entry;
        }
    }

    internal bool Contains(ushort id) => _tagged.ContainsKey(id);

    /// <summary>Encodes the properties: maps the names to identifiers and converts the strings of an ANSI file.</summary>
    internal List<WriterProperty> Encode(NameToIdMapWriter names, Encoding string8Encoding, bool ansi)
    {
        var result = new List<WriterProperty>(Count);
        foreach (var entry in _tagged.Values)
        {
            result.Add(EncodeEntry(entry.Id, entry, string8Encoding, ansi));
        }

        foreach (var (name, entry) in _named)
        {
            result.Add(EncodeEntry(names.GetId(name), entry, string8Encoding, ansi));
        }

        return result;
    }

    /// <summary>Returns the value of an entry; the bytes copied from another file are decoded (8-bit strings with the code page 1252).</summary>
    private static object? GetEntryValue(Entry entry) => entry.Value switch
    {
        RawValue raw => PropertyValueCodec.Decode(entry.Type, raw.Bytes, PstContext.GetEncoding(1252)),
        Func<Stream> => null,
        var value => value,
    };

    private static WriterProperty EncodeEntry(ushort id, Entry entry, Encoding string8Encoding, bool ansi)
    {
        switch (entry.Value)
        {
            case RawValue raw when ansi && entry.Type is MapiPropertyType.Unicode or MapiPropertyType.MultipleUnicode:
                // An ANSI file stores the strings in its code page
                var value = PropertyValueCodec.Decode(entry.Type, raw.Bytes, string8Encoding)!;
                var ansiType = entry.Type == MapiPropertyType.Unicode ? MapiPropertyType.String8 : MapiPropertyType.MultipleString8;
                return new WriterProperty(new MapiPropertyTag(id, ansiType), PropertyValueCodec.Encode(ansiType, value, string8Encoding));
            case RawValue raw:
                return new WriterProperty(new MapiPropertyTag(id, entry.Type), raw.Bytes);
            case Stream stream:
                return new WriterProperty(new MapiPropertyTag(id, entry.Type), null, stream);
            case Func<Stream> openStream:
                return new WriterProperty(new MapiPropertyTag(id, entry.Type), null, openStream());
        }

        var type = entry.Type;
        if (ansi)
        {
            type = type switch
            {
                MapiPropertyType.Unicode => MapiPropertyType.String8,
                MapiPropertyType.MultipleUnicode => MapiPropertyType.MultipleString8,
                _ => type,
            };
        }

        return new WriterProperty(new MapiPropertyTag(id, type), PropertyValueCodec.Encode(type, entry.Value, string8Encoding));
    }

    private sealed record Entry(ushort Id, MapiPropertyType Type, object Value);

    private sealed record RawValue(byte[] Bytes);
}
