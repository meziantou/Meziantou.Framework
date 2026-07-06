using System.Collections;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Stream.</summary>
public class YamlStream : YamlNode, IList<YamlDocument>
{
    private readonly List<YamlDocument> _documents;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlStream()
    {
        StreamStart = new StreamStart();
        StreamEnd = new StreamEnd();
        _documents = new List<YamlDocument>();
    }

    private YamlStream(StreamStart streamStart, StreamEnd streamEnd, List<YamlDocument> documents)
    {
        StreamStart = streamStart;
        StreamEnd = streamEnd;
        _documents = documents;
    }

    internal StreamStart StreamStart { get; }
    internal StreamEnd StreamEnd { get; }

    /// <summary>Loads data.</summary>
    public static YamlStream Load(TextReader stream)
    {
        return Load(new EventReader(Parser.CreateParser(stream)));
    }

    /// <summary>Loads data.</summary>
    public static YamlStream Load(EventReader eventReader)
    {
        var streamStart = eventReader.Expect<StreamStart>();

        var documents = new List<YamlDocument>();
        while (!eventReader.Accept<StreamEnd>())
            documents.Add(YamlDocument.Load(eventReader));

        var streamEnd = eventReader.Expect<StreamEnd>();

        return new YamlStream(streamStart, streamEnd, documents);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Gets enumerator.</summary>
    public IEnumerator<YamlDocument> GetEnumerator()
    {
        return _documents.GetEnumerator();
    }

    /// <summary>Adds an item.</summary>
    public void Add(YamlDocument item)
    {
        _documents.Add(item);
    }

    /// <summary>Removes all elements from the collection.</summary>
    public void Clear()
    {
        _documents.Clear();
    }

    /// <summary>Determines whether a value exists.</summary>
    public bool Contains(YamlDocument item)
    {
        return _documents.Contains(item);
    }

    /// <summary>Copies the elements to an array starting at the specified index.</summary>
    public void CopyTo(YamlDocument[] array, int arrayIndex)
    {
        _documents.CopyTo(array, arrayIndex);
    }

    /// <summary>Removes an item.</summary>
    public bool Remove(YamlDocument item)
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
    public int Count { get { return _documents.Count; } }

    /// <summary>Gets a value indicating whether is Read Only.</summary>
    public bool IsReadOnly { get { return false; } }

    /// <summary>Gets the zero-based index of the specified item.</summary>
    public int IndexOf(YamlDocument item)
    {
        return _documents.IndexOf(item);
    }

    /// <summary>Inserts an item at the specified index.</summary>
    public void Insert(int index, YamlDocument item)
    {
        _documents.Insert(index, item);
    }

    /// <summary>Removes at.</summary>
    public void RemoveAt(int index)
    {
        _documents.RemoveAt(index);
    }

    /// <summary>Gets or sets an element at the specified index.</summary>
    public YamlDocument this[int index]
    {
        get { return _documents[index]; }
        set
        {
            _documents[index] = value;
        }
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public override YamlNode DeepClone()
    {
        var documentsClone = new List<YamlDocument>(_documents.Count);
        for (var i = 0; i < _documents.Count; i++)
            documentsClone.Add((YamlDocument)_documents[i].DeepClone());

        return new YamlStream(StreamStart, StreamEnd, documentsClone);
    }
}
