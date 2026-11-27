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
        return Load(stream, options: null);
    }

    /// <summary>Loads data, honoring the hardening options in <paramref name="options"/>.</summary>
    /// <param name="stream">The reader over the YAML payload.</param>
    /// <param name="options">
    /// The options used to bound nesting depth and alias expansion. If <see langword="null"/>,
    /// <see cref="YamlSerializerOptions.Default"/> is used.
    /// </param>
    /// <remarks>
    /// Use this overload for untrusted input: it applies <see cref="YamlSerializerOptions.MaxDepth"/>,
    /// <see cref="YamlSerializerOptions.AllowAnchors"/>, <see cref="YamlSerializerOptions.AllowAliases"/> and
    /// <see cref="YamlSerializerOptions.MaxAliasExpansionNodeCount"/>.
    /// </remarks>
    public static YamlStream Load(TextReader stream, YamlSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var effectiveOptions = options ?? YamlSerializerOptions.Default;

        // The text is kept, so that the stream can be written back with only the nodes that changed rewritten
        var text = stream.ReadToEnd();
        var parser = Parser.CreateParser(new StringReader(text), effectiveOptions.EffectiveMaxDepth, effectiveOptions.SourceName);
        var result = Load(new EventReader(parser), effectiveOptions, trackSource: true);
        result.SourceText = text;
        return result;
    }

    /// <summary>Gets the text the stream was loaded from, if any.</summary>
    internal string? SourceText { get; private set; }

    /// <summary>
    /// Writes the stream as the text it was loaded from, in which only the nodes that changed since are rewritten. The
    /// rest of the text, comments, blank lines and formatting included, is copied byte for byte.
    /// </summary>
    /// <remarks>
    /// A changed scalar replaces the text of the scalar. An entry added to or removed from a block mapping or sequence
    /// adds or removes its lines. Other changes, such as reordered entries or a changed flow collection, rewrite the
    /// smallest node that contains them. A stream that was not loaded from a text is written like <see cref="YamlNode.WriteTo(TextWriter, bool)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WritePreservingFormatting(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToStringPreservingFormatting());
    }

    /// <summary>
    /// Returns the stream as the text it was loaded from, in which only the nodes that changed since are rewritten. See
    /// <see cref="WritePreservingFormatting(TextWriter)"/>.
    /// </summary>
    public string ToStringPreservingFormatting()
    {
        return YamlSourceEditor.Render(this);
    }

    /// <summary>Loads data.</summary>
    public static YamlStream Load(EventReader eventReader)
    {
        return Load(eventReader, options: null);
    }

    /// <summary>Loads data, honoring the anchor, alias and alias-expansion limits of <paramref name="options"/>.</summary>
    /// <param name="eventReader">The event reader.</param>
    /// <param name="options">
    /// The options used to bound alias expansion. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.
    /// </param>
    public static YamlStream Load(EventReader eventReader, YamlSerializerOptions? options)
    {
        return Load(eventReader, options, trackSource: false);
    }

    private static YamlStream Load(EventReader eventReader, YamlSerializerOptions? options, bool trackSource)
    {
        ArgumentNullException.ThrowIfNull(eventReader);

        // One context for the whole stream so alias expansion is bounded across every document, not per document.
        var context = YamlDocument.CreateContext(options);
        context.TrackSource = trackSource;

        var streamStart = eventReader.Expect<StreamStart>();

        var documents = new List<YamlDocument>();
        while (!eventReader.Accept<StreamEnd>())
            documents.Add(YamlDocument.Load(eventReader, context));

        var streamEnd = eventReader.Expect<StreamEnd>();

        return new YamlStream(streamStart, streamEnd, documents) { IsTracked = trackSource };
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
        OnChanging();
        _documents.Add(item);
    }

    /// <summary>Removes all elements from the collection.</summary>
    public void Clear()
    {
        OnChanging();
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
        OnChanging();
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
        OnChanging();
        _documents.Insert(index, item);
    }

    /// <summary>Removes at.</summary>
    public void RemoveAt(int index)
    {
        OnChanging();
        _documents.RemoveAt(index);
    }

    /// <summary>Gets or sets an element at the specified index.</summary>
    public YamlDocument this[int index]
    {
        get { return _documents[index]; }
        set
        {
            OnChanging();
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
