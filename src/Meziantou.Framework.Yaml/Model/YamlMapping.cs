using System.Collections;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Mapping.</summary>
public class YamlMapping : YamlContainer, IDictionary<YamlElement, YamlElement?>, IList<KeyValuePair<YamlElement, YamlElement?>>
{
    private MappingStart _mappingStart;
    private readonly List<YamlElement> _keys;
    private readonly Dictionary<YamlElement, YamlElement?> _contents;

    private Dictionary<string, YamlValue>? _stringKeys;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlMapping()
    {
        _mappingStart = new MappingStart();
        MappingEnd = new MappingEnd();
        _keys = new List<YamlElement>();
        _contents = new Dictionary<YamlElement, YamlElement?>();
    }

    private YamlMapping(MappingStart mappingStart, MappingEnd mappingEnd, List<YamlElement> keys, Dictionary<YamlElement, YamlElement?> contents)
    {
        _keys = keys;
        _contents = contents;
        MappingStart = mappingStart;
        MappingEnd = mappingEnd;
    }

    /// <summary>Gets mapping Start.</summary>
    public MappingStart MappingStart
    {
        get => _mappingStart;
        [MemberNotNull(nameof(_mappingStart))]
        set => _mappingStart = value;
    }

    internal MappingEnd MappingEnd { get; }

    /// <summary>Gets anchor.</summary>
    public override string? Anchor
    {
        get { return _mappingStart.Anchor; }
        set
        {
            MappingStart = new MappingStart(value,
                _mappingStart.Tag,
                _mappingStart.IsImplicit,
                _mappingStart.Style,
                _mappingStart.Start,
                _mappingStart.End);
        }
    }

    /// <summary>Gets tag.</summary>
    public override string? Tag
    {
        get { return _mappingStart.Tag; }
        set
        {
            MappingStart = new MappingStart(_mappingStart.Anchor,
                value,
                string.IsNullOrEmpty(value),
                _mappingStart.Style,
                _mappingStart.Start,
                _mappingStart.End);
        }
    }

    /// <summary>Gets style.</summary>
    public override YamlStyle Style
    {
        get { return _mappingStart.Style; }
        set
        {
            MappingStart = new MappingStart(_mappingStart.Anchor,
                _mappingStart.Tag,
                _mappingStart.IsImplicit,
                value,
                _mappingStart.Start,
                _mappingStart.End);
        }
    }

    /// <summary>Gets a value indicating whether is Canonical.</summary>
    public override bool IsCanonical { get { return _mappingStart.IsCanonical; } }

    /// <summary>Gets is Implicit.</summary>
    public override bool IsImplicit
    {
        get { return _mappingStart.IsImplicit; }
        set
        {
            MappingStart = new MappingStart(_mappingStart.Anchor,
                _mappingStart.Tag,
                value,
                _mappingStart.Style,
                _mappingStart.Start,
                _mappingStart.End);
        }
    }

    /// <summary>Loads data.</summary>
    public static YamlMapping Load(EventReader eventReader)
    {
        return Load(eventReader, anchors: null);
    }

    internal static YamlMapping Load(EventReader eventReader, Dictionary<string, YamlElement>? anchors)
    {
        var mappingStart = eventReader.Expect<MappingStart>();

        var keys = new List<YamlElement>();
        var contents = new Dictionary<YamlElement, YamlElement?>();
        while (!eventReader.Accept<MappingEnd>())
        {
            var key = ReadElement(eventReader, anchors);
            var value = ReadElement(eventReader, anchors);

            if (key is null || value is null)
            {
                throw new YamlException("Unexpected end of mapping while loading YAML model.");
            }

            keys.Add(key);
            contents[key] = value;
        }

        var mappingEnd = eventReader.Expect<MappingEnd>();

        return new YamlMapping(mappingStart, mappingEnd, keys, contents);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Gets enumerator.</summary>
    public IEnumerator<KeyValuePair<YamlElement, YamlElement?>> GetEnumerator()
    {
        return _keys.Select(k => new KeyValuePair<YamlElement, YamlElement?>(k, _contents[k])).GetEnumerator();
    }

    void ICollection<KeyValuePair<YamlElement, YamlElement?>>.Add(KeyValuePair<YamlElement, YamlElement?> item)
    {
        Add(item.Key, item.Value);
    }

    /// <summary>Removes all elements from the collection.</summary>
    public void Clear()
    {
        _contents.Clear();
        _keys.Clear();

        _stringKeys = null;
    }

    bool ICollection<KeyValuePair<YamlElement, YamlElement?>>.Contains(KeyValuePair<YamlElement, YamlElement?> item)
    {
        return _contents.ContainsKey(item.Key);
    }

    void ICollection<KeyValuePair<YamlElement, YamlElement?>>.CopyTo(KeyValuePair<YamlElement, YamlElement?>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<YamlElement, YamlElement?>>)_contents).CopyTo(array, arrayIndex);
    }

    bool ICollection<KeyValuePair<YamlElement, YamlElement?>>.Remove(KeyValuePair<YamlElement, YamlElement?> item)
    {
        return Remove(item.Key);
    }

    /// <summary>Gets count.</summary>
    public int Count { get { return _contents.Count; } }
    /// <summary>Gets a value indicating whether is Read Only.</summary>
    public bool IsReadOnly { get { return false; } }

    /// <summary>Adds an item.</summary>
    public void Add(YamlElement key, YamlElement? value)
    {
        _contents.Add(key, value);
        _keys.Add(key);

        if (_stringKeys != null && key is YamlValue value1)
        {
            _stringKeys[value1.Value] = value1;
        }
    }

    /// <summary>Determines whether key.</summary>
    public bool ContainsKey(YamlElement key)
    {
        return _contents.ContainsKey(key);
    }

    /// <summary>Determines whether key.</summary>
    public bool ContainsKey(string key)
    {
        if (_stringKeys == null)
            _stringKeys = Keys.OfType<YamlValue>().ToDictionary(k => k.Value, k => k, StringComparer.Ordinal);

        return _stringKeys.ContainsKey(key);
    }

    /// <summary>Removes an item.</summary>
    public bool Remove(YamlElement key)
    {
        var index = _keys.IndexOf(key);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }

        return false;
    }


    /// <summary>Removes an item.</summary>
    public bool Remove(string key)
    {
        if (_stringKeys == null)
            _stringKeys = Keys.OfType<YamlValue>().ToDictionary(k => k.Value, k => k, StringComparer.Ordinal);

        if (!_stringKeys.TryGetValue(key, out var yaml))
            return false;

        if (Remove(yaml))
        {
            _stringKeys.Remove(key);
            return true;
        }

        return false;
    }

    /// <summary>Tries to get Value.</summary>
    public bool TryGetValue(YamlElement key, [MaybeNullWhen(false)] out YamlElement value)
    {
        return _contents.TryGetValue(key, out value);
    }

    /// <summary>Tries to get Value.</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out YamlElement value)
    {
        if (_stringKeys == null)
            _stringKeys = Keys.OfType<YamlValue>().ToDictionary(k => k.Value, k => k, StringComparer.Ordinal);

        if (!_stringKeys.TryGetValue(key, out var yamlKey))
        {
            value = null;
            return false;
        }

        return TryGetValue(yamlKey, out value);
    }

    /// <summary>Gets or sets an element at the specified index.</summary>
    public YamlElement? this[YamlElement key]
    {
        get
        {
            _contents.TryGetValue(key, out var item);
            return item;
        }
        set
        {
            if (!_contents.ContainsKey(key))
            {
                _keys.Add(key);

                if (_stringKeys != null && key is YamlValue yamlValue)
                {
                    _stringKeys[yamlValue.Value] = yamlValue;
                }
            }

            _contents[key] = value;
        }
    }

    /// <summary>Gets or sets an element at the specified index.</summary>
    public YamlElement? this[string key]
    {
        get
        {
            if (_stringKeys == null)
                _stringKeys = Keys.OfType<YamlValue>().ToDictionary(k => k.Value, k => k, StringComparer.Ordinal);

            if (!_stringKeys.TryGetValue(key, out var stringKey))
                return null;

            return this[stringKey];
        }
        set
        {
            if (_stringKeys == null)
                _stringKeys = Keys.OfType<YamlValue>().ToDictionary(k => k.Value, k => k, StringComparer.Ordinal);

            if (!_stringKeys.ContainsKey(key))
                _stringKeys[key] = new YamlValue(key);

            this[_stringKeys[key]] = value;
        }
    }

    /// <summary>Gets keys.</summary>
    public ICollection<YamlElement> Keys { get { return _keys; } }
    /// <summary>Gets values.</summary>
    public ICollection<YamlElement?> Values { get { return _contents.Values; } }

    /// <summary>Gets the zero-based index of the specified item.</summary>
    public int IndexOf(KeyValuePair<YamlElement, YamlElement?> item)
    {
        return _keys.IndexOf(item.Key);
    }

    /// <summary>Inserts an item at the specified index.</summary>
    public void Insert(int index, KeyValuePair<YamlElement, YamlElement?> item)
    {
        if (_contents.ContainsKey(item.Key))
            throw new ArgumentException("Key already present", nameof(item));

        _keys.Insert(index, item.Key);
        _contents[item.Key] = item.Value;

        if (_stringKeys != null && item.Key is YamlValue yamlValue)
        {
            _stringKeys[yamlValue.Value] = yamlValue;
        }
    }

    /// <summary>Removes at.</summary>
    public void RemoveAt(int index)
    {
        var key = _keys[index];

        _keys.RemoveAt(index);
        _contents.Remove(key);

        if (_stringKeys != null && key is YamlValue value1)
        {
            _stringKeys.Remove(value1.Value);
        }

    }

    /// <summary>Gets or sets an element at the specified index.</summary>
    public KeyValuePair<YamlElement, YamlElement?> this[int index]
    {
        get { return new KeyValuePair<YamlElement, YamlElement?>(_keys[index], _contents[_keys[index]]); }
        set
        {
            if (_keys[index] != value.Key && _contents.ContainsKey(value.Key))
                throw new ArgumentException("Key already present at a different index.", nameof(value));

            var oldKey = _keys[index];

            if (_keys[index] != value.Key)
            {
                _contents.Remove(_keys[index]);
            }

            if (_stringKeys != null && oldKey is YamlValue yamlValue)
            {
                _stringKeys[yamlValue.Value] = yamlValue;
            }

            if (_stringKeys != null && value.Key is YamlValue key)
            {
                _stringKeys[key.Value] = key;
            }

            _keys[index] = value.Key;
            _contents[value.Key] = value.Value;

        }
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public override YamlNode DeepClone()
    {
        var keysClone = new List<YamlElement>(_keys.Count);
        for (var i = 0; i < _keys.Count; i++)
            keysClone.Add((YamlElement)_keys[i].DeepClone());

        var cloneContents = new Dictionary<YamlElement, YamlElement?>();

        for (var i = 0; i < _keys.Count; i++)
        {
            var content = _contents[_keys[i]];
            cloneContents[keysClone[i]] = content is null ? null : (YamlElement)content.DeepClone();
        }

        return new YamlMapping(_mappingStart,
            MappingEnd,
            keysClone,
            cloneContents);
    }
}
