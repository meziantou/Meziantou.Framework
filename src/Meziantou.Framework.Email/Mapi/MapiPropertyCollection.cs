using System.Collections;
using System.Text;
using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>The MAPI properties of an object: a folder, a message, a recipient or an attachment.</summary>
public sealed class MapiPropertyCollection : IReadOnlyCollection<MapiProperty>
{
    private const ushort MessageCodePageId = 0x3FFD;

    private readonly List<MapiProperty> _properties;
    private readonly Dictionary<ushort, MapiProperty> _byId = [];

    internal MapiPropertyCollection(PropertySource source, IReadOnlyList<RawProperty> properties)
    {
        Source = source;
        var encoding = source.DefaultEncoding;
        foreach (var property in properties)
        {
            if (property.Tag.Id == MessageCodePageId && property.Tag.Type == MapiPropertyType.Integer32 && property.Inline is { Length: 4 } inline)
            {
                var codePage = BitConverter.ToInt32(inline);
                if (codePage > 0 && codePage < 65536)
                {
                    encoding = PstContext.GetEncoding(codePage);
                }
            }
        }

        Encoding = encoding;
        _properties = new List<MapiProperty>(properties.Count);
        foreach (var property in properties)
        {
            var item = new MapiProperty(source, property, encoding);
            _properties.Add(item);
            _byId.TryAdd(property.Tag.Id, item);
        }
    }

    internal PropertySource Source { get; }

    internal Encoding Encoding { get; }

    /// <summary>Gets the number of properties.</summary>
    public int Count => _properties.Count;

    /// <summary>Returns the property that has an identifier, whatever its type.</summary>
    /// <param name="id">The identifier of the property, such as 0x0037 for the subject.</param>
    /// <returns>The property, or <see langword="null"/> when the object does not have it.</returns>
    public MapiProperty? Find(ushort id) => _byId.GetValueOrDefault(id);

    /// <summary>Returns the property that has a tag.</summary>
    /// <param name="tag">The tag of the property.</param>
    /// <returns>The property, or <see langword="null"/> when the object does not have it.</returns>
    public MapiProperty? Find(MapiPropertyTag tag) => _byId.TryGetValue(tag.Id, out var property) && property.Type == tag.Type ? property : _properties.Find(p => p.Tag == tag);

    /// <summary>Returns a named property.</summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>The property, or <see langword="null"/> when the object does not have it or the file does not map the name.</returns>
    public MapiProperty? Find(MapiNamedProperty name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var id = Source.GetId(name);
        return id is null ? null : Find(id.Value);
    }

    /// <summary>Gets the value of a property, converted to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the value, such as <see cref="string"/>, <see cref="int"/> or <see cref="DateTime"/>.</typeparam>
    /// <param name="id">The identifier of the property.</param>
    /// <param name="value">The value, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the object has the property and its value is a <typeparamref name="T"/>.</returns>
    public bool TryGetValue<T>(ushort id, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) => TryGetValue(Find(id), out value);

    /// <summary>Gets the value of a named property, converted to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the value, such as <see cref="string"/>, <see cref="int"/> or <see cref="DateTime"/>.</typeparam>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The value, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the object has the property and its value is a <typeparamref name="T"/>.</returns>
    public bool TryGetValue<T>(MapiNamedProperty name, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) => TryGetValue(Find(name), out value);

    /// <summary>Gets the value of a property, converted to <typeparamref name="T"/>, or the default value of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="id">The identifier of the property.</param>
    /// <returns>The value, or <see langword="default"/> when the object does not have the property or its value has another type.</returns>
    public T? GetValueOrDefault<T>(ushort id) => TryGetValue<T>(id, out var value) ? value : default;

    /// <summary>Gets the value of a named property, converted to <typeparamref name="T"/>, or the default value of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value, or <see langword="default"/> when the object does not have the property or its value has another type.</returns>
    public T? GetValueOrDefault<T>(MapiNamedProperty name) => TryGetValue<T>(name, out var value) ? value : default;

    /// <inheritdoc />
    public IEnumerator<MapiProperty> GetEnumerator() => _properties.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal string? GetString(ushort id) => GetValueOrDefault<string>(id);

    internal string? GetString(MapiNamedProperty name) => GetValueOrDefault<string>(name);

    internal int? GetInt32(ushort id) => TryGetValue<int>(id, out var value) ? value : null;

    internal int? GetInt32(MapiNamedProperty name) => TryGetValue<int>(name, out var value) ? value : null;

    internal bool? GetBoolean(ushort id) => TryGetValue<bool>(id, out var value) ? value : null;

    internal bool? GetBoolean(MapiNamedProperty name) => TryGetValue<bool>(name, out var value) ? value : null;

    internal DateTime? GetDateTime(ushort id) => TryGetValue<DateTime>(id, out var value) ? value : null;

    internal DateTime? GetDateTime(MapiNamedProperty name) => TryGetValue<DateTime>(name, out var value) ? value : null;

    internal byte[]? GetBinary(ushort id) => GetValueOrDefault<byte[]>(id);

    internal byte[]? GetBinary(MapiNamedProperty name) => GetValueOrDefault<byte[]>(name);

    private static bool TryGetValue<T>(MapiProperty? property, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value)
    {
        if (property is not null)
        {
            var raw = property.Value;
            switch (raw)
            {
                case T typed:
                    value = typed;
                    return true;
                case short int16 when typeof(T) == typeof(int):
                    value = (T)(object)(int)int16;
                    return true;
                case int int32 when typeof(T) == typeof(long):
                    value = (T)(object)(long)int32;
                    return true;
                case int int32 when typeof(T) == typeof(bool):
                    value = (T)(object)(int32 != 0);
                    return true;
            }
        }

        value = default;
        return false;
    }
}
