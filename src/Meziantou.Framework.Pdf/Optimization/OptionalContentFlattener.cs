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
    private static readonly PdfName APName = PdfName.Create("AP");

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

    /// <summary>Gets a value indicating whether a content could not be flattened: visible text continues hidden text on the same line. The optional content properties must then be kept.</summary>
    public bool IsIncomplete { get; private set; }

    public static bool HasOptionalContent(PdfDocument document) => document.Catalog.ContainsKey(OCPropertiesName);

    public static void RemoveProperties(PdfDocument document) => document.Catalog.Remove(OCPropertiesName);

    /// <summary>Flattens a page: its content, its forms, its patterns and its annotations, with their appearances.</summary>
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
            {
                // The appearances of an annotation can have optional content of their own
                changed |= FlattenAppearances(annotation);
                continue;
            }

            if (IsHidden(group))
            {
                page.Annotations.Remove(annotation);
            }
            else
            {
                annotation.Dictionary.Remove(OCName);
                changed |= FlattenAppearances(annotation);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>Flattens the forms and the tiling patterns of resources, and those they use.</summary>
    private bool FlattenForms(PdfDictionary? resources, int depth)
    {
        if (depth > MaxFormDepth || resources is null)
            return false;

        var changed = false;
        if (resources.GetResolved(PdfNames.XObject) is PdfDictionary xobjects)
        {
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
                        var empty = CreateEmptyForm();
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

                changed |= FlattenContentStream(xobject, resources, depth);
            }
        }

        // The cells of the tiling patterns are contents too (8.7.3.1)
        if (resources.GetResolved(PdfNames.Pattern) is PdfDictionary patterns)
        {
            foreach (var name in patterns.Keys.ToList())
            {
                if (patterns.GetResolved(name) is PdfStream pattern && _processedForms.Add(pattern))
                {
                    changed |= FlattenContentStream(pattern, resources, depth);
                }
            }
        }

        return changed;
    }

    /// <summary>Flattens a content stream (a form, a pattern cell, an appearance) and the forms and patterns it uses.</summary>
    private bool FlattenContentStream(PdfStream stream, PdfDictionary? parentResources, int depth)
    {
        var changed = false;
        var dictionary = stream.Dictionary;
        var streamResources = dictionary.GetResolved(PdfNames.Resources) as PdfDictionary ?? parentResources;
        if (TryRewrite(stream.GetDecodedData(), streamResources, out var content))
        {
            stream.SetData(content);
            dictionary.Remove(PdfNames.Filter);
            dictionary.Remove(PdfNames.DecodeParms);
            changed = true;
        }

        changed |= FlattenForms(streamResources, depth + 1);
        return changed;
    }

    /// <summary>Flattens the appearance streams of an annotation (12.5.5): each appearance, and each state of an appearance.</summary>
    private bool FlattenAppearances(PdfAnnotation annotation)
    {
        if (annotation.Dictionary.GetResolved(APName) is not PdfDictionary appearances)
            return false;

        var changed = false;
        foreach (var key in appearances.Keys.ToList())
        {
            switch (appearances.GetResolved(key))
            {
                case PdfStream stream:
                    changed |= FlattenAppearance(appearances, key, stream);
                    break;

                case PdfDictionary states:
                    foreach (var state in states.Keys.ToList())
                    {
                        if (states.GetResolved(state) is PdfStream stateStream)
                        {
                            changed |= FlattenAppearance(states, state, stateStream);
                        }
                    }

                    break;
            }
        }

        return changed;
    }

    private bool FlattenAppearance(PdfDictionary parent, PdfName key, PdfStream stream)
    {
        if (!_processedForms.Add(stream))
            return false;

        if (stream.Dictionary.GetResolved(OCName) is { } group)
        {
            stream.Dictionary.Remove(OCName);
            if (IsHidden(group))
            {
                if (parent[key] is PdfReference reference)
                {
                    _document.Objects.Set(reference, CreateEmptyForm());
                }
                else
                {
                    parent[key] = CreateEmptyForm();
                }

                return true;
            }

            FlattenContentStream(stream, parentResources: null, depth: 0);
            return true;
        }

        return FlattenContentStream(stream, parentResources: null, depth: 0);
    }

    private static PdfStream CreateEmptyForm() => new(new PdfDictionary
    {
        [PdfNames.Type] = PdfNames.XObject,
        [PdfNames.Subtype] = PdfName.Create("Form"),
        [PdfNames.BBox] = PdfArray.FromNumbers(0, 0, 0, 0),
    });

    /// <summary>
    /// Removes the painting of the hidden marked-content sequences of a content, and turns the visible ones into
    /// <c>/OC BMC</c>. A hidden sequence still changes the graphics and text state (8.11.3.3): its state operators are
    /// kept, and its paths are ended without painting (<c>n</c>, which keeps the clipping). Its text is removed, but not
    /// the moves to the next line of <c>'</c> and <c>"</c>: the text position after it is unknown until a new line
    /// (<c>Td</c>, <c>TD</c>, <c>T*</c>, <c>Tm</c>) or a new text object.
    /// </summary>
    /// <param name="data">The content.</param>
    /// <param name="resources">The resources of the content.</param>
    /// <param name="content">The new content.</param>
    /// <returns><see langword="true"/> when the content has optional content and was rewritten. <see langword="false"/> when it has none, or when visible text continues hidden text on the same line, whose position would change (<see cref="IsIncomplete"/> is then set).</returns>
    private bool TryRewrite(ReadOnlyMemory<byte> data, PdfDictionary? resources, out byte[] content)
    {
        var reader = new ContentStreamReader(data);
        var writer = new PdfByteWriter(Math.Max(256, data.Length));
        var operands = new List<PdfObject>();
        var stack = new Stack<bool>();
        var unknownTextPosition = false;
        var changed = false;
        content = [];
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

                case "BT" or "Td" or "TD" or "T*" or "Tm" or "'" or "\"":
                    unknownTextPosition = false;
                    break;

                case "Tj" or "TJ" when !hidden && unknownTextPosition:
                    IsIncomplete = true;
                    return false;
            }

            if (hidden)
            {
                switch (name)
                {
                    // Painting: nothing is drawn, the state does not change
                    case "BMC" or "BDC" or "EMC" or "MP" or "DP" or "sh" or "Do" or "BI":
                        continue;

                    // The path is ended without painting: a clipping (W, W*) still applies
                    case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                        ContentStreamWriter.Write(writer, "n", []);
                        continue;

                    // The text is removed: the position after it is unknown
                    case "Tj" or "TJ":
                        unknownTextPosition = true;
                        continue;

                    case "'":
                        ContentStreamWriter.Write(writer, "T*", []);
                        unknownTextPosition = true;
                        continue;

                    case "\"" when operands is [var wordSpacing, var characterSpacing, ..]:
                        ContentStreamWriter.Write(writer, "Tw", [wordSpacing]);
                        ContentStreamWriter.Write(writer, "Tc", [characterSpacing]);
                        ContentStreamWriter.Write(writer, "T*", []);
                        unknownTextPosition = true;
                        continue;
                }
            }

            if (name == "BI" && operands is [PdfDictionary image, PdfString imageData, ..])
            {
                ContentStreamWriter.Write(writer, name, [image], imageData.Bytes.Span);
            }
            else
            {
                ContentStreamWriter.Write(writer, name, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(operands));
            }
        }

        if (changed)
        {
            content = writer.ToArray();
        }

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
