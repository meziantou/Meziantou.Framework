using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Optimization;

/// <summary>
/// Makes the optional content of a document permanent (ISO 32000-2, 8.11): the content of the groups hidden in the
/// default configuration (<c>/D</c> of <c>/OCProperties</c>) is removed, and the content of the visible groups is kept
/// without its group. The marked-content sequences of a visible group become <c>/OC BMC</c>, so that the nesting of the
/// sequences does not change.
/// </summary>
internal sealed class OptionalContentFlattener
{
    private const int MaxFormDepth = 16;

    private static readonly PdfName OCPropertiesName = PdfName.Create("OCProperties");
    private static readonly PdfName OCName = PdfName.Create("OC");
    private static readonly PdfName OCGsName = PdfName.Create("OCGs");
    private static readonly PdfName OCMDName = PdfName.Create("OCMD");
    private static readonly PdfName BaseStateName = PdfName.Create("BaseState");
    private static readonly PdfName OnName = PdfName.Create("ON");
    private static readonly PdfName OffName = PdfName.Create("OFF");
    private static readonly PdfName DName = PdfName.Create("D");
    private static readonly PdfName PName = PdfName.Create("P");

    private readonly HashSet<PdfObject> _hidden = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfObject> _processedForms = new(ReferenceEqualityComparer.Instance);
    private readonly PdfDocument _document;

    public OptionalContentFlattener(PdfDocument document)
    {
        _document = document;
        if (document.Catalog.GetResolved(OCPropertiesName) is not PdfDictionary properties)
            return;

        var configuration = properties.GetResolved(DName) as PdfDictionary;
        if (configuration?.GetName(BaseStateName)?.Value == "OFF")
        {
            // Every group is hidden, except those listed in /ON
            var on = ResolveAll(configuration.GetResolved(OnName) as PdfArray);
            foreach (var group in ResolveAll(properties.GetResolved(OCGsName) as PdfArray))
            {
                if (!on.Contains(group))
                {
                    _hidden.Add(group);
                }
            }
        }
        else if (configuration is not null)
        {
            _hidden.UnionWith(ResolveAll(configuration.GetResolved(OffName) as PdfArray));
        }
    }

    public static bool HasOptionalContent(PdfDocument document) => document.Catalog.ContainsKey(OCPropertiesName);

    public static void RemoveProperties(PdfDocument document) => document.Catalog.Remove(OCPropertiesName);

    /// <summary>Flattens a page: its content, its forms and its annotations.</summary>
    /// <returns><see langword="true"/> when the page changed.</returns>
    public bool FlattenPage(PdfPage page)
    {
        var changed = false;
        var resources = page.Resources;
        if (TryRewrite(page.GetContentData(), resources, out var content))
        {
            var stream = new PdfStream(new PdfDictionary(), content);
            page.Dictionary[PdfNames.Contents] = _document.Objects.Add(stream);
            changed = true;
        }

        changed |= FlattenForms(resources, depth: 0);
        foreach (var annotation in page.Annotations.ToList())
        {
            var group = annotation.Dictionary.GetResolved(OCName);
            if (group is null)
                continue;

            if (IsHidden(group))
            {
                page.Annotations.Remove(annotation);
            }
            else
            {
                annotation.Dictionary.Remove(OCName);
            }

            changed = true;
        }

        return changed;
    }

    private bool FlattenForms(PdfDictionary? resources, int depth)
    {
        if (depth > MaxFormDepth || resources?.GetResolved(PdfNames.XObject) is not PdfDictionary xobjects)
            return false;

        var changed = false;
        foreach (var name in xobjects.Keys.ToList())
        {
            if (xobjects.GetResolved(name) is not PdfStream xobject || !_processedForms.Add(xobject))
                continue;

            var dictionary = xobject.Dictionary;
            if (dictionary.GetResolved(OCName) is { } group)
            {
                dictionary.Remove(OCName);
                changed = true;
                if (IsHidden(group))
                {
                    // An empty form draws nothing: the operators that paint it stay valid
                    var empty = new PdfStream(new PdfDictionary
                    {
                        [PdfNames.Type] = PdfNames.XObject,
                        [PdfNames.Subtype] = PdfName.Create("Form"),
                        [PdfNames.BBox] = PdfArray.FromNumbers(0, 0, 0, 0),
                    });
                    if (xobjects[name] is PdfReference reference)
                    {
                        _document.Objects.Set(reference, empty);
                    }
                    else
                    {
                        xobjects[name] = empty;
                    }

                    continue;
                }
            }

            if (dictionary.GetName(PdfNames.Subtype)?.Value != "Form")
                continue;

            var formResources = dictionary.GetResolved(PdfNames.Resources) as PdfDictionary ?? resources;
            if (TryRewrite(xobject.GetDecodedData(), formResources, out var content))
            {
                xobject.SetData(content);
                dictionary.Remove(PdfNames.Filter);
                dictionary.Remove(PdfNames.DecodeParms);
                changed = true;
            }

            changed |= FlattenForms(formResources, depth + 1);
        }

        return changed;
    }

    /// <summary>Removes the hidden marked-content sequences of a content, and turns the visible ones into <c>/OC BMC</c>.</summary>
    /// <returns><see langword="true"/> when the content has optional content.</returns>
    private bool TryRewrite(ReadOnlyMemory<byte> data, PdfDictionary? resources, out byte[] content)
    {
        var reader = new ContentStreamReader(data);
        var writer = new PdfByteWriter(Math.Max(256, data.Length));
        var operands = new List<PdfObject>();
        var stack = new Stack<bool>();
        var changed = false;
        while (reader.TryRead(operands, out var name))
        {
            var hidden = stack.Count > 0 && stack.Peek();
            switch (name)
            {
                case "BMC":
                    stack.Push(hidden);
                    break;

                case "BDC":
                    if (operands is [PdfName tag, var properties, ..] && tag == OCName)
                    {
                        changed = true;
                        var group = properties is PdfName propertyName && resources?.GetResolved(PdfNames.Properties) is PdfDictionary list ? list.GetResolved(propertyName) : properties;
                        var isHidden = hidden || (group is not null && IsHidden(group));
                        stack.Push(isHidden);
                        if (!isHidden)
                        {
                            ContentStreamWriter.Write(writer, "BMC", [OCName]);
                        }

                        continue;
                    }

                    stack.Push(hidden);
                    break;

                case "EMC":
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    break;
            }

            if (hidden)
                continue;

            if (name == "BI" && operands is [PdfDictionary image, PdfString imageData, ..])
            {
                ContentStreamWriter.Write(writer, name, [image], imageData.Bytes.Span);
            }
            else
            {
                ContentStreamWriter.Write(writer, name, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(operands));
            }
        }

        content = changed ? writer.ToArray() : [];
        return changed;
    }

    /// <summary>Evaluates an optional content group or membership dictionary (8.11.2) in the default configuration. The visibility expressions (<c>/VE</c>) are not evaluated: the policy is used.</summary>
    private bool IsHidden(PdfObject group)
    {
        if (group is not PdfDictionary dictionary)
            return false;

        if (!dictionary.IsType(OCMDName))
            return _hidden.Contains(dictionary);

        var groups = dictionary.GetResolved(OCGsName) switch
        {
            PdfDictionary single => [single],
            PdfArray array => ResolveAll(array).ToList(),
            _ => [],
        };
        if (groups.Count == 0)
            return false;

        var hiddenCount = groups.Count(_hidden.Contains);
        return dictionary.GetName(PName)?.Value switch
        {
            "AllOn" => hiddenCount > 0,
            "AnyOff" => hiddenCount == 0,
            "AllOff" => hiddenCount != groups.Count,
            _ => hiddenCount == groups.Count, // AnyOn
        };
    }

    private static HashSet<PdfObject> ResolveAll(PdfArray? array)
    {
        var result = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        if (array is null)
            return result;

        for (var i = 0; i < array.Count; i++)
        {
            if (array.TryGetResolved<PdfDictionary>(i, out var group))
            {
                result.Add(group);
            }
        }

        return result;
    }
}
