using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>The indirect objects of a document (ISO 32000-2, 7.3.10): the objects that references point to.</summary>
/// <remarks>
/// The objects of a file are loaded when a reference is first resolved. The collection is not thread-safe: a document
/// supports one operation at a time.
/// </remarks>
public sealed class PdfIndirectObjectCollection
{
    private const int MaxLoadDepth = 32;

    private readonly Dictionary<int, IndirectObjectEntry> _entries = [];
    private int _maxObjectNumber;
    private int _loadDepth;

    internal PdfIndirectObjectCollection()
    {
    }

    /// <summary>Gets the number of indirect objects, loaded or not.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the highest object number in use.</summary>
    public int MaxObjectNumber => _maxObjectNumber;

    internal IPdfObjectLoader? Loader { get; set; }

    internal IEnumerable<IndirectObjectEntry> Entries => _entries.Values;

    /// <summary>Adds an object as a new indirect object.</summary>
    /// <param name="value">The object. It cannot be a reference.</param>
    /// <returns>The reference to the new object.</returns>
    public PdfReference Add(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is PdfReference)
            throw new ArgumentException("An indirect object cannot be a reference", nameof(value));

        var number = checked(_maxObjectNumber + 1);
        var entry = new IndirectObjectEntry(new PdfReference(this, number, 0))
        {
            Value = value,
            State = IndirectObjectState.Loaded,
            IsNew = true,
        };
        _entries[number] = entry;
        _maxObjectNumber = number;
        return entry.Reference;
    }

    /// <summary>Replaces the value of an indirect object.</summary>
    /// <param name="reference">The reference to the object.</param>
    /// <param name="value">The new value. It cannot be a reference.</param>
    public void Set(PdfReference reference, PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(value);
        if (value is PdfReference)
            throw new ArgumentException("An indirect object cannot be a reference", nameof(value));

        var entry = GetEntry(reference) ?? throw new ArgumentException("The reference does not point to an object of this collection", nameof(reference));
        entry.Value = value;
        entry.State = IndirectObjectState.Loaded;
    }

    /// <summary>Gets the object a reference points to.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The object, or <see cref="PdfNull.Instance"/> when it does not exist or cannot be read.</returns>
    public PdfObject Resolve(PdfReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!ReferenceEquals(reference.Owner, this))
            throw new ArgumentException("The reference belongs to another document", nameof(reference));

        var entry = GetEntry(reference);
        if (entry is null)
            return PdfNull.Instance;

        return Load(entry);
    }

    /// <summary>Gets the reference to an object by its number.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="reference">The reference, with the generation of the object.</param>
    /// <returns><see langword="true"/> when the object exists.</returns>
    public bool TryGetReference(int objectNumber, [NotNullWhen(true)] out PdfReference? reference)
    {
        if (_entries.TryGetValue(objectNumber, out var entry) && entry.State is not IndirectObjectState.Free)
        {
            reference = entry.Reference;
            return true;
        }

        reference = null;
        return false;
    }

    /// <summary>Gets the references to every indirect object, in the order of their numbers.</summary>
    /// <returns>The references.</returns>
    public IEnumerable<PdfReference> GetReferences()
    {
        foreach (var entry in _entries.Values.Where(e => e.State is not IndirectObjectState.Free).OrderBy(e => e.Reference.ObjectNumber))
        {
            yield return entry.Reference;
        }
    }

    /// <summary>Gets or creates the reference the parser reads (<c>12 0 R</c>).</summary>
    internal PdfReference GetOrCreateReference(int objectNumber, int generation)
    {
        if (_entries.TryGetValue(objectNumber, out var entry) && entry.Reference.Generation == generation)
            return entry.Reference;

        return new PdfReference(this, objectNumber, generation);
    }

    /// <summary>Registers an object of a file, read when a reference to it is resolved.</summary>
    internal IndirectObjectEntry Register(int objectNumber, int generation)
    {
        var entry = new IndirectObjectEntry(new PdfReference(this, objectNumber, generation));
        _entries[objectNumber] = entry;
        if (objectNumber > _maxObjectNumber)
        {
            _maxObjectNumber = objectNumber;
        }

        return entry;
    }

    internal IndirectObjectEntry? GetEntry(PdfReference reference)
    {
        if (_entries.TryGetValue(reference.ObjectNumber, out var entry) && entry.Reference.Generation == reference.Generation && entry.State is not IndirectObjectState.Free)
            return entry;

        return null;
    }

    internal IndirectObjectEntry? GetEntry(int objectNumber) => _entries.GetValueOrDefault(objectNumber);

    internal void Reserve(int maxObjectNumber)
    {
        if (maxObjectNumber > _maxObjectNumber)
        {
            _maxObjectNumber = maxObjectNumber;
        }
    }

    internal PdfObject Load(IndirectObjectEntry entry)
    {
        switch (entry.State)
        {
            case IndirectObjectState.Loaded:
                return entry.Value!;
            case IndirectObjectState.Loading:
                // A cycle: the object refers to itself while it is read (the length of its own stream)
                return PdfNull.Instance;
            case IndirectObjectState.Free:
                return PdfNull.Instance;
        }

        if (Loader is null)
            return PdfNull.Instance;

        // Reading an object can resolve another one (the /Length of a stream), which can resolve another one: a chain
        // of such objects is cut, so that a file cannot make the reader recurse without bound. The object stays
        // unloaded, and a later resolution from a shallower place reads it.
        if (_loadDepth >= MaxLoadDepth)
            return PdfNull.Instance;

        entry.State = IndirectObjectState.Loading;
        _loadDepth++;
        PdfObject value;
        try
        {
            value = Loader.Load(entry);
        }
        catch
        {
            entry.State = IndirectObjectState.Unloaded;
            throw;
        }
        finally
        {
            _loadDepth--;
        }

        entry.Value = value;
        entry.State = IndirectObjectState.Loaded;
        entry.OriginalFingerprint = PdfObjectFingerprint.Compute(value);
        return value;
    }
}
