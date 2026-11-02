using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The fields of the interactive form and the transforms of the signatures: DocMDP (ISO 32000-2, 12.8.2.2) and FieldMDP
/// (12.8.2.4), read by the verifier and written by the signer.
/// </summary>
internal static class PdfSignatureFields
{
    public const int MaxFieldDepth = 32;

    public static readonly PdfName FieldsName = PdfName.Create("Fields");
    public static readonly PdfName FieldTypeName = PdfName.Create("FT");
    public static readonly PdfName FieldNameName = PdfName.Create("T");
    public static readonly PdfName ValueName = PdfName.Create("V");
    public static readonly PdfName ParentName = PdfName.Create("Parent");
    public static readonly PdfName ByteRangeName = PdfName.Create("ByteRange");
    public static readonly PdfName SubFilterName = PdfName.Create("SubFilter");
    public static readonly PdfName ReferenceName = PdfName.Create("Reference");
    public static readonly PdfName TransformMethodName = PdfName.Create("TransformMethod");
    public static readonly PdfName TransformParamsName = PdfName.Create("TransformParams");
    public static readonly PdfName PermsName = PdfName.Create("Perms");
    public static readonly PdfName DocMdpName = PdfName.Create("DocMDP");
    public static readonly PdfName FieldMdpName = PdfName.Create("FieldMDP");
    public static readonly PdfName ActionName = PdfName.Create("Action");
    public static readonly PdfName LockName = PdfName.Create("Lock");
    public static readonly PdfName WidgetName = PdfName.Create("Widget");
    public static readonly PdfName SigFlagsName = PdfName.Create("SigFlags");
    public static readonly PdfName DssName = PdfName.Create("DSS");
    public static readonly PdfName AppearanceName = PdfName.Create("AP");

    /// <summary>Enumerates the fields of the form, parents first, in the order of the fields array (12.7.4.2: the name and the type are inherited).</summary>
    public static List<PdfFieldEntry> GetFields(PdfDocument document)
    {
        var result = new List<PdfFieldEntry>();
        if (document.Catalog.GetResolved(PdfNames.AcroForm) is PdfDictionary form && form.GetResolved(FieldsName) is PdfArray fields)
        {
            var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            foreach (var field in fields)
            {
                Visit(field, parentName: null, inheritedType: null, visited, result, depth: 0);
            }
        }

        return result;
    }

    private static void Visit(PdfObject item, string? parentName, string? inheritedType, HashSet<PdfDictionary> visited, List<PdfFieldEntry> result, int depth)
    {
        if (PdfReference.ResolveIfReference(item) is not PdfDictionary field || depth > MaxFieldDepth || !visited.Add(field))
            return;

        var partialName = field.GetText(FieldNameName);
        var name = partialName is null ? parentName : parentName is null ? partialName : parentName + "." + partialName;
        var type = field.GetName(FieldTypeName)?.Value ?? inheritedType;

        // A widget without a name is a widget of its parent field, not a field (12.7.4.1)
        if (partialName is not null || depth == 0 || field.ContainsKey(FieldTypeName))
        {
            result.Add(new PdfFieldEntry(name ?? "", field, item as PdfReference, type, depth));
        }

        if (field.GetResolved(PdfNames.Kids) is PdfArray kids)
        {
            foreach (var kid in kids)
            {
                Visit(kid, name, type, visited, result, depth + 1);
            }
        }
    }

    /// <summary>Gets a value indicating whether a signature dictionary is a document time stamp (12.8.5).</summary>
    public static bool IsDocumentTimestamp(PdfDictionary signature) => signature.GetName(PdfNames.Type)?.Value == "DocTimeStamp" || signature.GetName(SubFilterName)?.Value == "ETSI.RFC3161";

    /// <summary>Gets the certification level of the document: the DocMDP signature of the permissions of the catalog (12.8.4.2).</summary>
    public static PdfCertificationLevel? GetDocumentCertificationLevel(PdfDocument document)
    {
        if (document.Catalog.GetResolved(PermsName) is PdfDictionary permissions && permissions.GetResolved(DocMdpName) is PdfDictionary signature)
            return GetCertificationLevel(signature);

        return null;
    }

    /// <summary>Gets the <c>/P</c> of the DocMDP transform of a signature (table 257: 2 when it is missing).</summary>
    public static PdfCertificationLevel? GetCertificationLevel(PdfDictionary signature)
    {
        if (GetTransformParameters(signature, "DocMDP") is not { } parameters)
            return null;

        return parameters.GetInteger(PdfNames.P, 2) switch
        {
            1 => PdfCertificationLevel.NoChanges,
            3 => PdfCertificationLevel.FormFillingAndAnnotations,
            _ => PdfCertificationLevel.FormFilling,
        };
    }

    /// <summary>Gets the fields the FieldMDP transform of a signature locks (table 258).</summary>
    public static PdfFieldLock? GetFieldLock(PdfDictionary signature) => GetTransformParameters(signature, "FieldMDP") is { } parameters ? ReadLock(parameters) : null;

    /// <summary>Reads a FieldMDP transform parameters dictionary or a signature field lock dictionary (tables 258 and 236).</summary>
    public static PdfFieldLock? ReadLock(PdfDictionary dictionary)
    {
        var action = dictionary.GetName(ActionName)?.Value switch
        {
            "All" => PdfFieldLockAction.All,
            "Include" => PdfFieldLockAction.Include,
            "Exclude" => PdfFieldLockAction.Exclude,
            _ => (PdfFieldLockAction?)null,
        };
        if (action is not { } value)
            return null;

        var fields = new List<string>();
        if (dictionary.GetResolved(FieldsName) is PdfArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array.TryGetResolved<PdfString>(i, out var name))
                {
                    fields.Add(name.ToText());
                }
            }
        }

        // An Include or an Exclude without fields locks nothing or everything
        if (value != PdfFieldLockAction.All && fields.Count == 0)
            return value == PdfFieldLockAction.Exclude ? new PdfFieldLock(PdfFieldLockAction.All) : null;

        return new PdfFieldLock(value, fields);
    }

    private static PdfDictionary? GetTransformParameters(PdfDictionary signature, string method)
    {
        if (signature.GetResolved(ReferenceName) is not PdfArray references)
            return null;

        foreach (var item in references)
        {
            if (PdfReference.ResolveIfReference(item) is PdfDictionary reference && reference.GetName(TransformMethodName)?.Value == method)
                return reference.GetResolved(TransformParamsName) as PdfDictionary ?? [];
        }

        return null;
    }

    /// <summary>The signature reference dictionary of a DocMDP transform (tables 255 and 257).</summary>
    public static PdfDictionary CreateDocMdpReference(PdfCertificationLevel level) => new()
    {
        [PdfNames.Type] = PdfName.Create("SigRef"),
        [TransformMethodName] = DocMdpName,
        [TransformParamsName] = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("TransformParams"),
            [PdfNames.P] = PdfInteger.Get((int)level),
            [PdfName.Create("V")] = PdfName.Create("1.2"),
        },
    };

    /// <summary>The signature reference dictionary of a FieldMDP transform (tables 255 and 258); its data is the catalog.</summary>
    public static PdfDictionary CreateFieldMdpReference(PdfFieldLock fieldLock, PdfReference? catalog)
    {
        var parameters = CreateLockDictionary(fieldLock, "TransformParams");
        parameters[PdfName.Create("V")] = PdfName.Create("1.2");
        var reference = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("SigRef"),
            [TransformMethodName] = FieldMdpName,
            [TransformParamsName] = parameters,
        };
        if (catalog is not null)
        {
            reference[PdfName.Create("Data")] = catalog;
        }

        return reference;
    }

    /// <summary>A field lock dictionary (<c>SigFieldLock</c>, table 236) or the parameters of a FieldMDP transform.</summary>
    public static PdfDictionary CreateLockDictionary(PdfFieldLock fieldLock, string type)
    {
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create(type),
            [ActionName] = PdfName.Create(fieldLock.Action.ToString()),
        };
        if (fieldLock.Action != PdfFieldLockAction.All)
        {
            var fields = new PdfArray();
            foreach (var field in fieldLock.Fields)
            {
                fields.Add(PdfString.FromText(field));
            }

            dictionary[FieldsName] = fields;
        }

        return dictionary;
    }
}
