using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// Finds the objects that the revisions appended after a signed revision added or changed, and what each change is: the
/// cross-reference entries of the two revisions are compared, then the objects whose entry changed, and each changed
/// object is classified from the structure of the document (the form fields, the annotations of the pages, the document
/// security store).
/// </summary>
/// <remarks>
/// A dictionary that holds others (the catalog, the interactive form, a page, an array of annotations or of fields) is
/// classified by the entries that changed: a page whose only change is a new widget of a signature is a signature.
/// </remarks>
internal sealed class PdfModificationAnalyzer
{
    private const int MaxReachableDepth = 16;
    private const int MaxReachableObjects = 100_000;

    private readonly PdfDocument _signed;
    private readonly PdfDocument _current;
    private readonly Dictionary<int, (PdfModificationKind Kind, string? FieldName, bool IsField)> _known = [];
    private readonly HashSet<int> _pages = [];
    private readonly HashSet<int> _annotationArrays = [];
    private readonly int _catalogNumber;
    private readonly int _formNumber;
    private readonly int _fieldsNumber;
    private int _reachableBudget = MaxReachableObjects;

    private PdfModificationAnalyzer(PdfDocument signed, PdfDocument current)
    {
        _signed = signed;
        _current = current;
        _catalogNumber = (current.Trailer[PdfNames.Root] as PdfReference)?.ObjectNumber ?? -1;
        _formNumber = (current.Catalog[PdfNames.AcroForm] as PdfReference)?.ObjectNumber ?? -1;
        _fieldsNumber = (current.Catalog.GetResolved(PdfNames.AcroForm) as PdfDictionary)?[PdfSignatureFields.FieldsName] is PdfReference fields ? fields.ObjectNumber : -1;
    }

    /// <summary>Compares the signed revision with the current document.</summary>
    public static List<PdfModification> Compare(PdfDocument signed, PdfDocument current)
    {
        var analyzer = new PdfModificationAnalyzer(signed, current);
        analyzer.MapStructure();
        return analyzer.CompareObjects();
    }

    /// <summary>Gets a value indicating whether a certification and a field lock allow a modification (12.8.2.2.2 and 12.8.2.4).</summary>
    public static bool IsPermitted(PdfModification modification, PdfCertificationLevel? level, PdfFieldLock? fieldLock)
    {
        if (level is { } certification)
        {
            var allowed = modification.Kind switch
            {
                // The validation data and the document time stamps of PAdES are allowed by every level (ETSI EN 319 142-1, 5.4)
                PdfModificationKind.ValidationData or PdfModificationKind.DocumentTimestamp => true,
                PdfModificationKind.Signature or PdfModificationKind.FormFilling => certification >= PdfCertificationLevel.FormFilling,
                PdfModificationKind.Annotation => certification >= PdfCertificationLevel.FormFillingAndAnnotations,
                _ => false,
            };
            if (!allowed)
                return false;
        }

        // A locked field keeps the value and the appearance it had when the document was signed
        if (fieldLock is not null && !modification.IsNewObject && modification.FieldName is { } fieldName && modification.Kind is PdfModificationKind.FormFilling or PdfModificationKind.Signature && fieldLock.IsLocked(fieldName))
            return false;

        return true;
    }

    private static int Severity(PdfModificationKind kind) => kind switch
    {
        PdfModificationKind.ValidationData or PdfModificationKind.DocumentTimestamp => 0,
        _ => (int)kind - 1,
    };

    private static PdfModificationKind Max(PdfModificationKind? left, PdfModificationKind right) => left is { } value && Severity(value) >= Severity(right) ? value : right;

    /// <summary>Maps the objects of the current document to what they are: the signatures and the fields, the annotations, the document security store.</summary>
    private void MapStructure()
    {
        foreach (var field in PdfSignatureFields.GetFields(_current))
        {
            var kind = PdfModificationKind.FormFilling;
            var value = field.Field.GetResolved(PdfSignatureFields.ValueName);
            if (field.Type == "Sig")
            {
                kind = value is PdfDictionary signature && PdfSignatureFields.IsDocumentTimestamp(signature) ? PdfModificationKind.DocumentTimestamp : PdfModificationKind.Signature;
                if (field.Field[PdfSignatureFields.ValueName] is PdfReference signatureReference)
                {
                    MarkReachable(signatureReference, kind, field.Name);
                }
            }

            if (field.Reference is { } reference)
            {
                _known.TryAdd(reference.ObjectNumber, (kind, field.Name, IsField: true));
            }

            // The widgets of the field, and their appearances
            MarkAppearances(field.Field, kind, field.Name);
            if (field.Field.GetResolved(PdfNames.Kids) is PdfArray kids)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfReference kidReference && kidReference.Resolve() is PdfDictionary widget && !widget.ContainsKey(PdfSignatureFields.FieldNameName))
                    {
                        _known.TryAdd(kidReference.ObjectNumber, (kind, field.Name, IsField: false));
                        MarkAppearances(widget, kind, field.Name);
                    }
                }
            }
        }

        if (_current.Catalog[PdfSignatureFields.DssName] is { } store)
        {
            MarkReachable(store, PdfModificationKind.ValidationData, fieldName: null);
        }

        foreach (var page in _current.Pages)
        {
            _pages.Add(page.Reference.ObjectNumber);
            if (page.Dictionary[PdfNames.Annots] is PdfReference annotationsReference)
            {
                _annotationArrays.Add(annotationsReference.ObjectNumber);
            }

            if (page.Dictionary.GetResolved(PdfNames.Annots) is not PdfArray annotations)
                continue;

            foreach (var annotation in annotations)
            {
                if (annotation is PdfReference annotationReference && !_known.ContainsKey(annotationReference.ObjectNumber))
                {
                    MarkReachable(annotationReference, PdfModificationKind.Annotation, fieldName: null);
                }
            }
        }
    }

    private void MarkAppearances(PdfDictionary widget, PdfModificationKind kind, string? fieldName)
    {
        if (widget[PdfSignatureFields.AppearanceName] is { } appearances)
        {
            MarkReachable(appearances, kind, fieldName);
        }
    }

    /// <summary>Marks the objects reachable from an object that are not marked yet, within a bound.</summary>
    private void MarkReachable(PdfObject root, PdfModificationKind kind, string? fieldName)
    {
        var stack = new Stack<(PdfObject Value, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0 && _reachableBudget > 0)
        {
            var (value, depth) = stack.Pop();
            _reachableBudget--;
            if (depth > MaxReachableDepth)
                continue;

            switch (value)
            {
                case PdfReference reference:
                    // The parents of the fields and the pages are not part of what is reachable from an annotation
                    if (reference.ObjectNumber == _catalogNumber || _pages.Contains(reference.ObjectNumber) || !_known.TryAdd(reference.ObjectNumber, (kind, fieldName, IsField: false)))
                        continue;

                    stack.Push((reference.Resolve(), depth + 1));
                    break;

                case PdfArray array:
                    foreach (var item in array)
                    {
                        stack.Push((item, depth + 1));
                    }

                    break;

                case PdfDictionary dictionary:
                    foreach (var (key, item) in dictionary)
                    {
                        if (key != PdfNames.P && key != PdfSignatureFields.ParentName && key != PdfName.Create("Data"))
                        {
                            stack.Push((item, depth + 1));
                        }
                    }

                    break;

                case PdfStream stream:
                    stack.Push((stream.Dictionary, depth + 1));
                    break;
            }
        }
    }

    private List<PdfModification> CompareObjects()
    {
        var result = new List<PdfModification>();
        foreach (var entry in _current.Objects.Entries.Where(entry => entry.State is not IndirectObjectState.Free).OrderBy(entry => entry.Reference.ObjectNumber))
        {
            var number = entry.Reference.ObjectNumber;
            var old = _signed.Objects.GetEntry(number);
            var isNew = old is null || old.State is IndirectObjectState.Free || old.Reference.Generation != entry.Reference.Generation;
            if (!isNew && old!.Offset == entry.Offset && old.ContainerNumber == entry.ContainerNumber && old.IndexInContainer == entry.IndexInContainer)
                continue;

            var value = _current.Objects.Resolve(entry.Reference);
            var oldValue = isNew ? null : _signed.Objects.Resolve(old!.Reference);
            if (value is PdfNull || (oldValue is not null && AreEqual(oldValue, value)))
                continue;

            // The structure of the file: the objects of the object streams are compared one by one
            if (value is PdfStream { Dictionary: var streamDictionary } && streamDictionary.GetName(PdfNames.Type)?.Value is "XRef" or "ObjStm")
                continue;

            var (kind, fieldName) = Classify(number, value, oldValue);
            if (kind is { } modificationKind)
            {
                result.Add(new PdfModification(number, modificationKind, fieldName, isNew));
            }
        }

        // The objects the current document no longer has
        foreach (var entry in _signed.Objects.Entries.Where(entry => entry.State is not IndirectObjectState.Free))
        {
            if (_current.Objects.GetEntry(entry.Reference.ObjectNumber) is null)
            {
                result.Add(new PdfModification(entry.Reference.ObjectNumber, PdfModificationKind.Other, fieldName: null, isNewObject: false));
            }
        }

        return result;
    }

    private (PdfModificationKind? Kind, string? FieldName) Classify(int number, PdfObject value, PdfObject? oldValue)
    {
        if (number == _catalogNumber)
            return (CompareEntries(oldValue as PdfDictionary, value as PdfDictionary, ClassifyCatalogEntry), null);

        if (number == _formNumber)
            return (CompareEntries(oldValue as PdfDictionary, value as PdfDictionary, ClassifyFormEntry), null);

        if (number == _fieldsNumber)
            return (CompareArrays(oldValue as PdfArray, value as PdfArray, removed: PdfModificationKind.Other), null);

        if (_pages.Contains(number))
            return (CompareEntries(oldValue as PdfDictionary, value as PdfDictionary, ClassifyPageEntry), null);

        if (_annotationArrays.Contains(number))
            return (CompareArrays(oldValue as PdfArray, value as PdfArray, removed: PdfModificationKind.Annotation), null);

        if (_known.TryGetValue(number, out var known))
        {
            // A new field is not the filling of a form
            if (known.IsField && known.Kind == PdfModificationKind.FormFilling && oldValue is null)
                return (PdfModificationKind.Other, known.FieldName);

            return (known.Kind, known.FieldName);
        }

        return (PdfModificationKind.Other, null);
    }

    private PdfModificationKind? ClassifyCatalogEntry(PdfName key, PdfObject? oldValue, PdfObject? value) => key.Value switch
    {
        "DSS" or "Extensions" => PdfModificationKind.ValidationData,
        "Perms" => PdfModificationKind.Signature,
        "AcroForm" when oldValue is null || (oldValue is PdfReference && value is PdfReference) => ClassifyNewForm(value),
        "AcroForm" => CompareEntries(PdfReference.ResolveIfReference(oldValue ?? PdfNull.Instance) as PdfDictionary, PdfReference.ResolveIfReference(value ?? PdfNull.Instance) as PdfDictionary, ClassifyFormEntry),
        _ => PdfModificationKind.Other,
    };

    /// <summary>A form added to the catalog: what its fields are.</summary>
    private PdfModificationKind? ClassifyNewForm(PdfObject? value)
    {
        if (PdfReference.ResolveIfReference(value ?? PdfNull.Instance) is not PdfDictionary form)
            return PdfModificationKind.Other;

        PdfModificationKind? result = null;
        if (form.GetResolved(PdfSignatureFields.FieldsName) is PdfArray fields)
        {
            foreach (var field in fields)
            {
                result = Max(result, GetAddedKind(field));
            }
        }

        return result ?? PdfModificationKind.Signature;
    }

    private PdfModificationKind? ClassifyFormEntry(PdfName key, PdfObject? oldValue, PdfObject? value) => key.Value switch
    {
        "Fields" => CompareArrays(PdfReference.ResolveIfReference(oldValue ?? PdfNull.Instance) as PdfArray, PdfReference.ResolveIfReference(value ?? PdfNull.Instance) as PdfArray, removed: PdfModificationKind.Other),
        "SigFlags" => PdfModificationKind.Signature,
        "DR" or "DA" or "NeedAppearances" => PdfModificationKind.FormFilling,
        _ => PdfModificationKind.Other,
    };

    private PdfModificationKind? ClassifyPageEntry(PdfName key, PdfObject? oldValue, PdfObject? value)
    {
        if (key != PdfNames.Annots)
            return PdfModificationKind.Other;

        // An array of annotations that is an object of its own is compared on its own
        if (oldValue is PdfReference oldReference && value is PdfReference reference && oldReference.ObjectNumber == reference.ObjectNumber)
            return null;

        return CompareArrays(PdfReference.ResolveIfReference(oldValue ?? PdfNull.Instance) as PdfArray, PdfReference.ResolveIfReference(value ?? PdfNull.Instance) as PdfArray, removed: PdfModificationKind.Annotation);
    }

    /// <summary>The most significant change of the entries of a dictionary: <see langword="null"/> when no entry changed.</summary>
    private static PdfModificationKind? CompareEntries(PdfDictionary? old, PdfDictionary? current, Func<PdfName, PdfObject?, PdfObject?, PdfModificationKind?> classify)
    {
        if (old is null || current is null)
            return PdfModificationKind.Other;

        PdfModificationKind? result = null;
        foreach (var (key, value) in current)
        {
            var oldValue = old[key];
            if (oldValue is null || !AreEqual(oldValue, value))
            {
                if (classify(key, oldValue, value) is { } kind)
                {
                    result = Max(result, kind);
                }
            }
        }

        foreach (var (key, oldValue) in old)
        {
            if (!current.ContainsKey(key) && classify(key, oldValue, null) is { } kind)
            {
                result = Max(result, kind);
            }
        }

        return result;
    }

    /// <summary>The most significant change of an array of references: what the items added are, and the kind of a removal.</summary>
    private PdfModificationKind? CompareArrays(PdfArray? old, PdfArray? current, PdfModificationKind removed)
    {
        current ??= [];
        old ??= [];
        var oldNumbers = new HashSet<int>();
        foreach (var item in old)
        {
            if (item is not PdfReference reference)
                return PdfModificationKind.Other;

            oldNumbers.Add(reference.ObjectNumber);
        }

        PdfModificationKind? result = null;
        var currentNumbers = new HashSet<int>();
        foreach (var item in current)
        {
            if (item is not PdfReference reference)
                return PdfModificationKind.Other;

            currentNumbers.Add(reference.ObjectNumber);
            if (!oldNumbers.Contains(reference.ObjectNumber))
            {
                result = Max(result, GetAddedKind(reference));
            }
        }

        if (!oldNumbers.IsSubsetOf(currentNumbers))
        {
            result = Max(result, removed);
        }

        return result;
    }

    /// <summary>What an item added to an array of fields or of annotations is: a new field that is not a signature is another change.</summary>
    private PdfModificationKind GetAddedKind(PdfObject item)
    {
        if (item is not PdfReference reference || !_known.TryGetValue(reference.ObjectNumber, out var known))
            return PdfModificationKind.Other;

        return known.Kind == PdfModificationKind.FormFilling && _signed.Objects.GetEntry(reference.ObjectNumber) is null ? PdfModificationKind.Other : known.Kind;
    }

    /// <summary>Gets a value indicating whether two objects are written the same; streams have the same data.</summary>
    private static bool AreEqual(PdfObject left, PdfObject right)
    {
        if (left is PdfStream leftStream && right is PdfStream rightStream)
            return AreEqual(leftStream.Dictionary, rightStream.Dictionary) && leftStream.EncodedData.Span.SequenceEqual(rightStream.EncodedData.Span);

        if (left.GetType() != right.GetType())
            return false;

        try
        {
            var leftWriter = new PdfByteWriter(64);
            var rightWriter = new PdfByteWriter(64);
            PdfObjectSerializer.Write(leftWriter, left, ReferenceWriter.Identity);
            PdfObjectSerializer.Write(rightWriter, right, ReferenceWriter.Identity);
            return leftWriter.WrittenSpan.SequenceEqual(rightWriter.WrittenSpan);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
