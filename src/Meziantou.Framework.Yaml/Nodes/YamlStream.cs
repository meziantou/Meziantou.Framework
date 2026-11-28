using System.Collections;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>A YAML stream: a list of <see cref="YamlStreamDocument"/>.</summary>
public class YamlStream : IList<YamlStreamDocument>, IYamlModelNode
{
    private readonly List<YamlStreamDocument> _documents;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlStream()
    {
        StreamStart = new StreamStart();
        StreamEnd = new StreamEnd();
        _documents = new List<YamlStreamDocument>();
    }

    private YamlStream(StreamStart streamStart, StreamEnd streamEnd, List<YamlStreamDocument> documents)
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
    /// smallest node that contains them. A stream that was not loaded from a text is written like <see cref="WriteTo(TextWriter, bool)"/>.
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
        var context = YamlStreamDocument.CreateContext(options);
        context.TrackSource = trackSource;

        var streamStart = eventReader.Expect<StreamStart>();

        var documents = new List<YamlStreamDocument>();
        while (!eventReader.Accept<StreamEnd>())
            documents.Add(YamlStreamDocument.Load(eventReader, context));

        var streamEnd = eventReader.Expect<StreamEnd>();

        return new YamlStream(streamStart, streamEnd, documents) { IsTracked = trackSource };
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Gets enumerator.</summary>
    public IEnumerator<YamlStreamDocument> GetEnumerator()
    {
        return _documents.GetEnumerator();
    }

    /// <summary>Adds an item.</summary>
    public void Add(YamlStreamDocument item)
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
    public bool Contains(YamlStreamDocument item)
    {
        return _documents.Contains(item);
    }

    /// <summary>Copies the elements to an array starting at the specified index.</summary>
    public void CopyTo(YamlStreamDocument[] array, int arrayIndex)
    {
        _documents.CopyTo(array, arrayIndex);
    }

    /// <summary>Removes an item.</summary>
    public bool Remove(YamlStreamDocument item)
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
    public int IndexOf(YamlStreamDocument item)
    {
        return _documents.IndexOf(item);
    }

    /// <summary>Inserts an item at the specified index.</summary>
    public void Insert(int index, YamlStreamDocument item)
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
    public YamlStreamDocument this[int index]
    {
        get { return _documents[index]; }
        set
        {
            OnChanging();
            _documents[index] = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the stream was loaded from a text that keeps its changes, see <see cref="ToStringPreservingFormatting"/>.</summary>
    internal bool IsTracked { get; set; }

    /// <summary>Gets or sets the state of the stream before its first change, or <see langword="null"/> while it is unchanged.</summary>
    internal YamlNodeSnapshot? Snapshot { get; set; }

    bool IYamlModelNode.IsTracked { get => IsTracked; set => IsTracked = value; }

    YamlNodeSnapshot? IYamlModelNode.Snapshot { get => Snapshot; set => Snapshot = value; }

    /// <summary>Records the state of the stream before its first change.</summary>
    internal void OnChanging()
    {
        if (IsTracked && Snapshot is null)
        {
            Snapshot = YamlSourceEditor.CaptureCurrent(this);
        }
    }

    /// <summary>Enumerates the parsing events of the stream.</summary>
    public IEnumerable<ParsingEvent> EnumerateEvents()
    {
        return new YamlNodeEventEnumerator(this);
    }

    /// <summary>Writes the stream as YAML.</summary>
    public void WriteTo(TextWriter writer, bool suppressDocumentTags = false)
    {
        WriteTo(new Emitter(writer), suppressDocumentTags);
    }

    /// <summary>Writes the stream as YAML.</summary>
    public void WriteTo(IEmitter emitter, bool suppressDocumentTags = false)
    {
        YamlModelText.WriteTo(this, emitter, suppressDocumentTags);
    }

    /// <summary>Returns a string representation of the current instance.</summary>
    public override string ToString()
    {
        return YamlModelText.ToYaml(this).Trim();
    }

    /// <summary>Converts the stream to an instance of <typeparamref name="T"/>.</summary>
    public T? ToObject<T>(YamlSerializerOptions? options = null)
    {
        return (T?)ToObject(typeof(T), options);
    }

    /// <summary>Converts the stream to an object.</summary>
    public object? ToObject(Type type, YamlSerializerOptions? options = null)
    {
        return YamlModelText.ToObject(this, type, options);
    }

    /// <summary>Creates a deep clone of the current value.</summary>
    public YamlStream DeepClone()
    {
        var documentsClone = new List<YamlStreamDocument>(_documents.Count);
        for (var i = 0; i < _documents.Count; i++)
            documentsClone.Add(_documents[i].DeepClone());

        return new YamlStream(StreamStart, StreamEnd, documentsClone);
    }
}
