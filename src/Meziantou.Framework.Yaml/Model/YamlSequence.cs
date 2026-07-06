using System.Collections;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Sequence.</summary>
public class YamlSequence : YamlContainer, IList<YamlElement>
{
    private SequenceStart _sequenceStart;
    private readonly List<YamlElement> _contents;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlSequence()
    {
        _sequenceStart = new SequenceStart();
        SequenceEnd = new SequenceEnd();
        _contents = new List<YamlElement>();
    }

    private YamlSequence(SequenceStart sequenceStart, SequenceEnd sequenceEnd, List<YamlElement> contents)
    {
        _contents = contents;
        SequenceStart = sequenceStart;
        SequenceEnd = sequenceEnd;
    }

    /// <summary>Gets sequence Start.</summary>
    public SequenceStart SequenceStart
    {
        get => _sequenceStart;
        [MemberNotNull(nameof(_sequenceStart))]
        set
        {
            _sequenceStart = value;
        }
    }

    internal SequenceEnd SequenceEnd { get; }

    /// <summary>Gets anchor.</summary>
    public override string? Anchor
    {
        get { return _sequenceStart.Anchor; }
        set
        {
            SequenceStart = new SequenceStart(value,
                _sequenceStart.Tag,
                _sequenceStart.IsImplicit,
                _sequenceStart.Style,
                _sequenceStart.Start,
                _sequenceStart.End);
        }
    }

    /// <summary>Gets tag.</summary>
    public override string? Tag
    {
        get { return _sequenceStart.Tag; }
        set
        {
            SequenceStart = new SequenceStart(_sequenceStart.Anchor,
                value,
                string.IsNullOrEmpty(value),
                _sequenceStart.Style,
                _sequenceStart.Start,
                _sequenceStart.End);
        }
    }

    /// <summary>Gets style.</summary>
    public override YamlStyle Style
    {
        get { return _sequenceStart.Style; }
        set
        {
            SequenceStart = new SequenceStart(_sequenceStart.Anchor,
                _sequenceStart.Tag,
                _sequenceStart.IsImplicit,
                value,
                _sequenceStart.Start,
                _sequenceStart.End);
        }
    }

    /// <summary>Gets a value indicating whether is Canonical.</summary>
    public override bool IsCanonical { get { return _sequenceStart.IsCanonical; } }

    /// <summary>Gets is Implicit.</summary>
    public override bool IsImplicit
    {
        get { return _sequenceStart.IsImplicit; }
        set
        {
            SequenceStart = new SequenceStart(_sequenceStart.Anchor,
                _sequenceStart.Tag,
                value,
                _sequenceStart.Style,
                _sequenceStart.Start,
                _sequenceStart.End);
        }
    }

    /// <summary>Loads data.</summary>
    public static YamlSequence Load(EventReader eventReader)
    {
        return Load(eventReader, anchors: null);
    }

    internal static YamlSequence Load(EventReader eventReader, Dictionary<string, YamlElement>? anchors)
    {
        var sequenceStart = eventReader.Expect<SequenceStart>();

        var contents = new List<YamlElement>();
        while (!eventReader.Accept<SequenceEnd>())
        {
            var item = ReadElement(eventReader, anchors);
            if (item != null)
                contents.Add(item);
        }

        var sequenceEnd = eventReader.Expect<SequenceEnd>();

        return new YamlSequence(sequenceStart, sequenceEnd, contents);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Gets enumerator.</summary>
    public IEnumerator<YamlElement> GetEnumerator()
    {
        return _contents.GetEnumerator();
    }

    /// <summary>Adds an item.</summary>
    public void Add(YamlElement item)
    {
        _contents.Add(item);
    }

    /// <summary>Removes all elements from the collection.</summary>
    public void Clear()
    {
        _contents.Clear();
    }

    /// <summary>Determines whether a value exists.</summary>
    public bool Contains(YamlElement item)
    {
        return _contents.Contains(item);
    }

    /// <summary>Copies the elements to an array starting at the specified index.</summary>
    public void CopyTo(YamlElement[] array, int arrayIndex)
    {
        _contents.CopyTo(array, arrayIndex);
    }

    /// <summary>Removes an item.</summary>
    public bool Remove(YamlElement item)
    {
        var index = IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }

        return false;
    }

    /// <summary>Gets count.</summary>
    public int Count { get { return _contents.Count; } }

    /// <summary>Gets a value indicating whether is Read Only.</summary>
    public bool IsReadOnly { get { return false; } }

    /// <summary>Gets the zero-based index of the specified item.</summary>
    public int IndexOf(YamlElement item)
    {
        return _contents.IndexOf(item);
    }

    /// <summary>Inserts an item at the specified index.</summary>
    public void Insert(int index, YamlElement item)
    {
        _contents.Insert(index, item);
    }

    /// <summary>Removes at.</summary>
    public void RemoveAt(int index)
    {
        _contents.RemoveAt(index);
    }

    /// <summary>Gets or sets an element at the specified index.</summary>
    public YamlElement this[int index]
    {
        get { return _contents[index]; }
        set
        {
            _contents[index] = value;
        }
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public override YamlNode DeepClone()
    {
        var contentsClone = new List<YamlElement>(_contents.Count);
        for (var i = 0; i < _contents.Count; i++)
            contentsClone.Add((YamlElement)_contents[i].DeepClone());

        return new YamlSequence(_sequenceStart, SequenceEnd, contentsClone);
    }
}
