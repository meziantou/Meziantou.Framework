using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A reference to an indirect object of a document (ISO 32000-2, 7.3.10), such as <c>12 0 R</c>.</summary>
/// <remarks>References are created by <see cref="PdfIndirectObjectCollection"/>: a reference belongs to the indirect objects of one document.</remarks>
public sealed class PdfReference : PdfObject, IEquatable<PdfReference>
{
    private const int MaxReferenceChain = 32;

    internal PdfReference(PdfIndirectObjectCollection owner, int objectNumber, int generation)
    {
        Owner = owner;
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    /// <summary>Gets the object number.</summary>
    public int ObjectNumber { get; }

    /// <summary>Gets the generation number.</summary>
    public int Generation { get; }

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Reference;

    internal PdfIndirectObjectCollection Owner { get; }

    /// <summary>Gets the object the reference points to, loading it from the file when needed.</summary>
    /// <returns>The object, or <see cref="PdfNull.Instance"/> when it does not exist (ISO 32000-2, 7.3.10).</returns>
    public PdfObject Resolve() => Owner.Resolve(this);

    /// <summary>Resolves a value when it is a reference, following a chain of references to references.</summary>
    internal static PdfObject ResolveIfReference(PdfObject value)
    {
        for (var i = 0; i < MaxReferenceChain && value is PdfReference reference; i++)
        {
            value = reference.Resolve();
        }

        return value is PdfReference ? PdfNull.Instance : value;
    }

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] PdfReference? other) => other is not null && ReferenceEquals(Owner, other.Owner) && other.ObjectNumber == ObjectNumber && other.Generation == Generation;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PdfReference);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ObjectNumber, Generation);
}
