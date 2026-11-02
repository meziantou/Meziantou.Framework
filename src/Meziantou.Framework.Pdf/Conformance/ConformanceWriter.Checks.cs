using System.Text;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Metadata;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Conformance;

/// <summary>
/// The checks of PDF/A on the output intents, the actions, the annotations, the interactive form and the device color
/// spaces. The rules are those of ISO 19005 as the validation profiles of veraPDF test them.
/// </summary>
internal static partial class ConformanceWriter
{
    private const int MaxDepth = 32;

    private static readonly PdfName OutputIntentName = PdfName.Create("OutputIntent");
    private static readonly PdfName PdfAOutputIntentName = PdfName.Create("GTS_PDFA1");
    private static readonly PdfName OutputConditionIdentifierName = PdfName.Create("OutputConditionIdentifier");
    private static readonly PdfName OutputConditionName = PdfName.Create("OutputCondition");
    private static readonly PdfName RegistryNameName = PdfName.Create("RegistryName");
    private static readonly PdfName InfoName = PdfName.Create("Info");
    private static readonly PdfName DestOutputProfileName = PdfName.Create("DestOutputProfile");
    private static readonly PdfName AdditionalActionsName = PdfName.Create("AA");
    private static readonly PdfName OpenActionName = PdfName.Create("OpenAction");
    private static readonly PdfName JavaScriptName = PdfName.Create("JavaScript");
    private static readonly PdfName FieldsName = PdfName.Create("Fields");
    private static readonly PdfName FieldTypeName = PdfName.Create("FT");
    private static readonly PdfName FieldNameName = PdfName.Create("T");
    private static readonly PdfName DefaultAppearanceName = PdfName.Create("DA");
    private static readonly PdfName DefaultResourcesName = PdfName.Create("DR");
    private static readonly PdfName NeedAppearancesName = PdfName.Create("NeedAppearances");
    private static readonly PdfName InteriorColorName = PdfName.Create("IC");
    private static readonly PdfName PatternTypeName = PdfName.Create("PatternType");
    private static readonly PdfName DefaultRgbName = PdfName.Create("DefaultRGB");
    private static readonly PdfName DefaultCmykName = PdfName.Create("DefaultCMYK");
    private static readonly PdfName InlineImageMaskName = PdfName.Create("IM");

    /// <summary>
    /// Checks the output intents the document already has (ISO 19005-2, 6.2.3; ISO 19005-4, 6.2.3): they share one
    /// profile, of a printer or a monitor, in gray, RGB or CMYK, of version 2 for PDF/A-1 and below 5 for the other parts.
    /// </summary>
    /// <returns>The color space of the profile of the PDF/A output intent (<c>RGB </c>, <c>CMYK</c> or <c>GRAY</c>).</returns>
    private static string CheckOutputIntents(PdfDocument document, int part, List<string> errors)
    {
        const string Srgb = "RGB ";
        if (document.Catalog.Get<PdfArray>(OutputIntentsName) is not { } intents)
            return Srgb;

        PdfObject? first = null;
        var pdfAIntents = 0;
        foreach (var item in intents)
        {
            if (PdfReference.ResolveIfReference(item) is not PdfDictionary intent)
                continue;

            if (IsPdfAOutputIntent(intent))
            {
                pdfAIntents++;
                if (intent[DestOutputProfileName] is null)
                {
                    errors.Add("The PDF/A output intent needs a profile (DestOutputProfile)");
                }
            }

            if (intent[DestOutputProfileName] is not { } profile)
                continue;

            if (first is null)
            {
                first = profile;
            }
            else if (!IsSameObject(first, profile))
            {
                errors.Add($"PDF/A-{part} needs the same profile for every output intent (ISO 19005-{part}, {(part == 1 ? "6.2.2" : "6.2.3")}): the output intents of the document have different profiles");
            }
        }

        if (part == 4 && pdfAIntents > 1)
        {
            errors.Add("PDF/A-4 allows one PDF/A output intent (ISO 19005-4, 6.2.3)");
        }

        // Without a profile, the save adds the sRGB output intent
        if (Resolve(first) is not PdfStream stream)
            return Srgb;

        byte[] data;
        try
        {
            data = stream.GetDecodedData();
        }
        catch (PdfException)
        {
            errors.Add("The profile of the output intents cannot be decoded");
            return Srgb;
        }

        if (data.Length < 20)
        {
            errors.Add("The profile of the output intents is not an ICC profile");
            return Srgb;
        }

        var deviceClass = Encoding.Latin1.GetString(data, 12, 4);
        var colorSpace = Encoding.Latin1.GetString(data, 16, 4);
        if (deviceClass is not ("prtr" or "mntr") || colorSpace is not ("RGB " or "CMYK" or "GRAY"))
        {
            errors.Add($"PDF/A needs an output profile of a printer or a monitor in gray, RGB or CMYK: the profile of the output intents is a '{deviceClass.TrimEnd()}' profile in '{colorSpace.TrimEnd()}'");
        }

        if (part == 1 ? data[8] >= 3 : data[8] >= 5)
        {
            errors.Add(part == 1
                ? $"PDF/A-1 needs an output profile of version 2 (PDF 1.4): the profile of the output intents is of version {data[8]}"
                : $"PDF/A-{part} needs an output profile of version 2 or 4: the profile of the output intents is of version {data[8]}");
        }

        return colorSpace;
    }

    /// <summary>Resolves a value that may be missing.</summary>
    private static PdfObject? Resolve(PdfObject? value) => value is null ? null : PdfReference.ResolveIfReference(value);

    private static bool IsPdfAOutputIntent(PdfDictionary intent) => intent.GetName(PdfNames.S) == PdfAOutputIntentName;

    /// <summary>Gets the first output intent that has a profile, and the profile as it is written in the intent (a reference).</summary>
    private static (PdfDictionary Intent, PdfObject Value)? FindOutputProfile(PdfDocument document)
    {
        if (document.Catalog.Get<PdfArray>(OutputIntentsName) is not { } intents)
            return null;

        foreach (var item in intents)
        {
            if (PdfReference.ResolveIfReference(item) is PdfDictionary intent && intent[DestOutputProfileName] is { } value)
                return (intent, value);
        }

        return null;
    }

    private static bool IsSameObject(PdfObject left, PdfObject right) => (left, right) switch
    {
        (PdfReference a, PdfReference b) => a.ObjectNumber == b.ObjectNumber && a.Generation == b.Generation,
        _ => ReferenceEquals(PdfReference.ResolveIfReference(left), PdfReference.ResolveIfReference(right)),
    };

    /// <summary>
    /// Checks the actions (ISO 19005-1, 6.6; ISO 19005-2, 6.5 and 6.4.1; ISO 19005-4, 6.6 and 6.4.1): the types of
    /// actions each part allows, the four named actions of navigation, no action on the widgets, and no additional
    /// actions on the catalog, the pages, the fields and the widgets (PDF/A-4: only those of the annotations, which do
    /// not run on their own).
    /// </summary>
    private static void CheckActions(PdfDocument document, int part, List<string> errors)
    {
        string[] allowed = part switch
        {
            1 => ["GoTo", "GoToR", "Thread", "URI", "Named", "SubmitForm"],
            2 or 3 => ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"],
            _ => ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm", "JavaScript", "RichMediaExecute", "GoToDp"],
        };
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void CheckAction(PdfObject? value, string where, int depth)
        {
            if (depth > MaxDepth)
                return;

            switch (Resolve(value))
            {
                case PdfDictionary action when visited.Add(action):
                    if (action.GetName(PdfNames.S)?.Value is { } type)
                    {
                        if (!allowed.Contains(type, StringComparer.Ordinal))
                        {
                            errors.Add($"PDF/A-{part} does not allow the {type} actions: {where} has one");
                        }
                        else if (type == "Named" && action.GetName(PdfNames.N)?.Value is var name && name is not ("NextPage" or "PrevPage" or "FirstPage" or "LastPage"))
                        {
                            errors.Add($"PDF/A-{part} allows the NextPage, PrevPage, FirstPage and LastPage named actions only: {where} has the {name} named action");
                        }
                    }

                    CheckAction(action[PdfNames.Next], where, depth + 1);
                    break;
                case PdfArray actions:
                    foreach (var item in actions)
                    {
                        CheckAction(item, where, depth + 1);
                    }

                    break;
            }
        }

        // The additional actions of the fields and the widgets follow the rules of the interactive forms
        void CheckAdditionalActions(PdfDictionary owner, string where, bool isFormField)
        {
            if (owner.GetResolved(AdditionalActionsName) is not PdfDictionary additionalActions)
                return;

            if (part == 4)
            {
                // ISO 19005-4, 6.6.3: the triggers of the annotations only (cursor and focus)
                if (!isFormField && additionalActions.Any(entry => entry.Key.Value is not ("E" or "X" or "D" or "U" or "Fo" or "Bl")))
                {
                    errors.Add($"PDF/A-4 allows the E, X, D, U, Fo and Bl additional actions only: {where} has others (AA)");
                }
            }
            else
            {
                errors.Add($"PDF/A-{part} does not allow additional actions: {where} has some (AA)");
            }

            foreach (var (_, action) in additionalActions)
            {
                CheckAction(action, where, depth: 0);
            }
        }

        var catalog = document.Catalog;
        CheckAction(catalog.GetResolved(OpenActionName) as PdfDictionary, "the document (OpenAction)", depth: 0);
        CheckAdditionalActions(catalog, "the document", isFormField: false);
        if (catalog.Get<PdfDictionary>(PdfNames.Names)?.Get<PdfDictionary>(JavaScriptName) is { } scripts)
        {
            foreach (var action in EnumerateNameTreeValues(scripts))
            {
                CheckAction(action, "the JavaScript of the document", depth: 0);
            }
        }

        var pageNumber = 0;
        foreach (var page in document.Pages)
        {
            pageNumber++;
            CheckAdditionalActions(page.Dictionary, $"page {pageNumber}", isFormField: false);
            foreach (var annotation in page.Annotations)
            {
                var where = $"a {annotation.Subtype} annotation of page {pageNumber}";
                var isWidget = annotation.Subtype == "Widget";
                if (isWidget && annotation.Dictionary.ContainsKey(PdfNames.A))
                {
                    errors.Add($"PDF/A-{part} does not allow the actions of the widget annotations: {where} has one (A)");
                }

                CheckAction(annotation.Dictionary[PdfNames.A], where, depth: 0);
                CheckAdditionalActions(annotation.Dictionary, where, isFormField: isWidget);
            }
        }

        foreach (var (item, title) in EnumerateOutlineItems(document))
        {
            CheckAction(item[PdfNames.A], $"the outline item '{title}'", depth: 0);
        }

        foreach (var field in EnumerateFields(document))
        {
            if (field.GetName(PdfNames.Subtype)?.Value == "Widget")
                continue;

            var where = $"the form field '{field.GetText(FieldNameName)}'";
            CheckAction(field[PdfNames.A], where, depth: 0);
            CheckAdditionalActions(field, where, isFormField: true);
        }
    }

    /// <summary>
    /// Checks the annotations (ISO 19005-1, 6.5.3; ISO 19005-2, 6.3.3; ISO 19005-4, 6.3.3): an appearance for every
    /// annotation from PDF/A-2 (except the popups, the links, the projections of PDF/A-4 and the annotations without
    /// size), only the normal appearance, a stream (a dictionary of states for the buttons), and, for PDF/A-1, colors
    /// only with an RGB output intent.
    /// </summary>
    private static void CheckAnnotations(PdfDocument document, int part, string outputColorSpace, List<string> errors)
    {
        var clause = part == 1 ? "6.5.3" : "6.3.3";
        var pageNumber = 0;
        foreach (var page in document.Pages)
        {
            pageNumber++;
            foreach (var annotation in page.Annotations)
            {
                var dictionary = annotation.Dictionary;
                var subtype = annotation.Subtype;
                if (part == 1 && outputColorSpace != "RGB " && (dictionary.ContainsKey(PdfNames.C) || dictionary.ContainsKey(InteriorColorName)))
                {
                    errors.Add($"PDF/A-1 allows the colors of the annotations (C and IC) only with an RGB output intent (ISO 19005-1, 6.5.3): the {subtype} annotation of page {pageNumber} has one");
                }

                if (dictionary.GetResolved(AppearanceName) is not PdfDictionary appearances)
                {
                    if (part > 1 && subtype is not ("Popup" or "Link") && !(part == 4 && subtype == "Projection") && !HasNoSize(dictionary))
                    {
                        errors.Add($"PDF/A-{part} needs the appearance of the annotations (AP, ISO 19005-{part}, 6.3.3): the {subtype} annotation of page {pageNumber} has none");
                    }

                    continue;
                }

                if (appearances.Any(entry => entry.Key != PdfNames.N))
                {
                    errors.Add($"PDF/A-{part} allows only the normal appearance of the annotations (the N entry of AP, ISO 19005-{part}, {clause})");
                }

                var normal = appearances.GetResolved(PdfNames.N);
                if (subtype == "Widget" && GetFieldType(dictionary) == "Btn")
                {
                    if (normal is not PdfDictionary)
                    {
                        errors.Add($"PDF/A-{part} needs the normal appearance of a button to be a dictionary of states (ISO 19005-{part}, {clause}): the button of page {pageNumber} has none");
                    }
                }
                else if (normal is not PdfStream)
                {
                    errors.Add($"PDF/A-{part} needs the normal appearance of an annotation to be a stream (ISO 19005-{part}, {clause}): the {subtype} annotation of page {pageNumber} has none");
                }
            }
        }
    }

    private static bool HasNoSize(PdfDictionary annotation)
    {
        return annotation.GetResolved(PdfNames.Rect) is PdfArray { Count: 4 } rectangle
            && rectangle.GetNumber(0) == rectangle.GetNumber(2)
            && rectangle.GetNumber(1) == rectangle.GetNumber(3);
    }

    /// <summary>Checks the interactive form (ISO 19005-1, 6.9; ISO 19005-2, 6.4.1; ISO 19005-4, 6.4.1): the viewer does not generate the appearances of the fields.</summary>
    private static void CheckInteractiveForm(PdfDocument document, int part, List<string> errors)
    {
        if (document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)?.Get<PdfBoolean>(NeedAppearancesName)?.Value is true)
        {
            errors.Add($"PDF/A-{part} does not allow the NeedAppearances flag of the interactive form: the fields need appearances");
        }
    }

    /// <summary>
    /// Gets the fonts of the default resources of the interactive form (<c>/DR</c>) that a viewer renders fields with:
    /// the fonts the default appearance (<c>/DA</c>) of the widgets without an appearance names, or of every widget when
    /// the form asks the viewer to generate the appearances (<c>/NeedAppearances</c>).
    /// </summary>
    private static List<PdfObject> GetFormFonts(PdfDocument document)
    {
        var result = new List<PdfObject>();
        if (document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm) is not { } form || form.Get<PdfDictionary>(DefaultResourcesName)?.Get<PdfDictionary>(PdfNames.Font) is not { } fonts)
            return result;

        var needAppearances = form.Get<PdfBoolean>(NeedAppearancesName)?.Value is true;
        foreach (var field in EnumerateFields(document))
        {
            // The widgets are the leaves of the tree of the fields
            if (field.ContainsKey(PdfNames.Kids))
                continue;

            if (!needAppearances && field.Get<PdfDictionary>(AppearanceName)?.ContainsKey(PdfNames.N) is true)
                continue;

            var defaultAppearance = GetInheritedText(field, DefaultAppearanceName) ?? form.GetText(DefaultAppearanceName);
            if (defaultAppearance is not null && GetFontName(defaultAppearance) is { } name && fonts[name] is { } font)
            {
                result.Add(font);
            }
        }

        return result;
    }

    /// <summary>Gets the font a default appearance selects with the <c>Tf</c> operator.</summary>
    private static PdfName? GetFontName(string defaultAppearance)
    {
        var reader = new ContentStreamReader(Encoding.Latin1.GetBytes(defaultAppearance));
        var operands = new List<PdfObject>();
        PdfName? result = null;
        while (reader.TryRead(operands, out var name))
        {
            if (name == "Tf" && operands is [PdfName font, ..])
            {
                result = font;
            }
        }

        return result;
    }

    private static string? GetFieldType(PdfDictionary field) => GetInherited(field, FieldTypeName) is PdfName type ? type.Value : null;

    private static string? GetInheritedText(PdfDictionary field, PdfName key) => GetInherited(field, key) is PdfString text ? text.ToText() : null;

    /// <summary>Gets an inheritable entry of a field (ISO 32000-2, 12.7.4.1): from the field or its ancestors.</summary>
    private static PdfObject? GetInherited(PdfDictionary field, PdfName key)
    {
        var current = field;
        for (var i = 0; i < MaxDepth && current is not null; i++)
        {
            if (current.GetResolved(key) is { } value)
                return value;

            current = current.GetResolved(PdfNames.Parent) as PdfDictionary;
        }

        return null;
    }

    /// <summary>Gets the dictionaries of the tree of the fields of the interactive form: the fields and their widgets.</summary>
    private static List<PdfDictionary> EnumerateFields(PdfDocument document)
    {
        var result = new List<PdfDictionary>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Visit(PdfArray? fields, int depth)
        {
            if (fields is null || depth > MaxDepth)
                return;

            foreach (var item in fields)
            {
                if (PdfReference.ResolveIfReference(item) is PdfDictionary field && visited.Add(field))
                {
                    result.Add(field);
                    Visit(field.GetResolved(PdfNames.Kids) as PdfArray, depth + 1);
                }
            }
        }

        Visit(document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)?.GetResolved(FieldsName) as PdfArray, depth: 0);
        return result;
    }

    /// <summary>Gets the dictionaries of the outline items and their titles, as the catalog has them.</summary>
    private static List<(PdfDictionary Item, string? Title)> EnumerateOutlineItems(PdfDocument document)
    {
        var result = new List<(PdfDictionary, string?)>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Visit(PdfDictionary parent, int depth)
        {
            if (depth > MaxDepth)
                return;

            var item = parent.GetResolved(PdfNames.First) as PdfDictionary;
            while (item is not null && visited.Add(item))
            {
                result.Add((item, item.GetText(PdfNames.Title)));
                Visit(item, depth + 1);
                item = item.GetResolved(PdfNames.Next) as PdfDictionary;
            }
        }

        if (document.Catalog.Get<PdfDictionary>(PdfNames.Outlines) is { } outlines)
        {
            Visit(outlines, depth: 0);
        }

        return result;
    }

    /// <summary>Gets the values of a name tree (ISO 32000-2, 7.9.6).</summary>
    private static List<PdfObject> EnumerateNameTreeValues(PdfDictionary root)
    {
        var result = new List<PdfObject>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Visit(PdfDictionary node, int depth)
        {
            if (depth > MaxDepth || !visited.Add(node))
                return;

            if (node.GetResolved(PdfNames.Names) is PdfArray names)
            {
                for (var i = 1; i < names.Count; i += 2)
                {
                    result.Add(names[i]);
                }
            }

            if (node.GetResolved(PdfNames.Kids) is PdfArray kids)
            {
                foreach (var kid in kids)
                {
                    if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                    {
                        Visit(child, depth + 1);
                    }
                }
            }
        }

        Visit(root, depth: 0);
        return result;
    }

    /// <summary>
    /// Checks the use of the device color spaces (ISO 19005-1, 6.2.3.3; ISO 19005-2 and ISO 19005-4, 6.2.4.3):
    /// DeviceRGB needs an RGB output intent, DeviceCMYK a CMYK one, unless a <c>DefaultRGB</c> or <c>DefaultCMYK</c>
    /// color space replaces them; DeviceGray goes with any output intent. The content streams of the pages, forms,
    /// patterns, Type 3 glyphs and annotation appearances are read for the operators that select a color space.
    /// </summary>
    private static void CheckDeviceColorSpaces(PdfDocument document, int part, string outputColorSpace, List<string> errors)
    {
        var clause = part == 1 ? "6.2.3.3" : "6.2.4.3";
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var visitedStreams = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);

        void Use(string family, ColorScope scope)
        {
            var allowed = family switch
            {
                "DeviceRGB" => outputColorSpace == "RGB " || scope.HasDefaultRgb,
                "DeviceCMYK" => outputColorSpace == "CMYK" || scope.HasDefaultCmyk,
                _ => true,
            };
            if (!allowed && reported.Add(family + "\n" + scope.Where))
            {
                errors.Add($"PDF/A-{part} allows {family} only with an output intent of the same color space or a Default{family[6..]} color space (ISO 19005-{part}, {clause}): {scope.Where} uses {family} and the output intent is in {outputColorSpace.TrimEnd()} (use an ICC-based color space)");
            }
        }

        void UseColorSpace(PdfObject? value, ColorScope scope, int depth)
        {
            if (depth > 8)
                return;

            switch (Resolve(value))
            {
                case PdfName { Value: "DeviceRGB" or "RGB" }:
                    Use("DeviceRGB", scope);
                    break;
                case PdfName { Value: "DeviceCMYK" or "CMYK" }:
                    Use("DeviceCMYK", scope);
                    break;
                case PdfName name when scope.Resources?.Get<PdfDictionary>(PdfNames.ColorSpace)?.GetResolved(name) is { } named:
                    UseColorSpace(named, scope, depth + 1);
                    break;
                case PdfArray { Count: > 1 } array:
                    switch ((array.GetResolved(0) as PdfName)?.Value)
                    {
                        case "Indexed" or "I" or "Pattern":
                            UseColorSpace(array[1], scope, depth + 1);
                            break;
                        case "Separation" or "DeviceN" when array.Count > 2:
                            UseColorSpace(array[2], scope, depth + 1);
                            break;
                    }

                    break;
            }
        }

        void UseShading(PdfObject? value, ColorScope scope)
        {
            var shading = Resolve(value) switch
            {
                PdfStream stream => stream.Dictionary,
                PdfDictionary dictionary => dictionary,
                _ => null,
            };
            UseColorSpace(shading?[PdfNames.ColorSpace], scope, depth: 0);
        }

        void UseForm(PdfStream form, ColorScope parent, string? where, int depth)
        {
            var dictionary = form.Dictionary;
            var scope = parent.Enter(dictionary.GetResolved(PdfNames.Resources) as PdfDictionary, where);
            if (dictionary.Get<PdfDictionary>(PdfNames.Group) is { } group)
            {
                UseColorSpace(group[PdfNames.CS], scope, depth: 0);
            }

            ReadContent(form, scope, depth + 1);
        }

        void UseXObject(PdfObject? value, ColorScope scope, int depth)
        {
            if (Resolve(value) is not PdfStream xobject)
                return;

            var dictionary = xobject.Dictionary;
            if (dictionary.GetName(PdfNames.Subtype) == PdfNames.Form)
            {
                UseForm(xobject, scope, where: null, depth);
            }
            else if (dictionary.Get<PdfBoolean>(PdfNames.ImageMask)?.Value is not true)
            {
                UseColorSpace(dictionary[PdfNames.ColorSpace], scope, depth: 0);
            }
        }

        void UsePattern(PdfObject? value, ColorScope scope, int depth)
        {
            switch (Resolve(value))
            {
                case PdfStream tiling:
                    UseForm(tiling, scope, where: null, depth);
                    break;
                case PdfDictionary pattern when pattern.GetInteger(PatternTypeName) == 2:
                    UseShading(pattern[PdfNames.Shading], scope);
                    break;
            }
        }

        void ReadContent(PdfStream stream, ColorScope scope, int depth)
        {
            if (depth > MaxDepth || !visitedStreams.Add(stream))
                return;

            byte[] data;
            try
            {
                data = stream.GetDecodedData();
            }
            catch (PdfException)
            {
                return;
            }

            var resources = scope.Resources;
            var reader = new ContentStreamReader(data);
            var operands = new List<PdfObject>();
            while (reader.TryRead(operands, out var name))
            {
                switch (name)
                {
                    case "rg" or "RG":
                        Use("DeviceRGB", scope);
                        break;
                    case "k" or "K":
                        Use("DeviceCMYK", scope);
                        break;
                    case "cs" or "CS" when operands is [PdfName colorSpace]:
                        UseColorSpace(colorSpace, scope, depth: 0);
                        break;
                    case "sh" when operands is [PdfName shading]:
                        UseShading(resources?.Get<PdfDictionary>(PdfNames.Shading)?[shading], scope);
                        break;
                    case "scn" or "SCN" when operands is [.., PdfName pattern]:
                        UsePattern(resources?.Get<PdfDictionary>(PdfNames.Pattern)?[pattern], scope, depth);
                        break;
                    case "Do" when operands is [PdfName xobject]:
                        UseXObject(resources?.Get<PdfDictionary>(PdfNames.XObject)?[xobject], scope, depth);
                        break;
                    case "BI" when operands is [PdfDictionary image, ..]:
                        if (image.GetResolved(PdfNames.ImageMask) is not PdfBoolean { Value: true } && image.GetResolved(InlineImageMaskName) is not PdfBoolean { Value: true })
                        {
                            UseColorSpace(image[PdfNames.ColorSpace] ?? image[PdfNames.CS], scope, depth: 0);
                        }

                        break;
                    case "Tf" when operands is [PdfName fontName, ..]:
                        if (resources?.Get<PdfDictionary>(PdfNames.Font)?.GetResolved(fontName) is PdfDictionary font && font.GetName(PdfNames.Subtype) == PdfNames.Type3 && font.Get<PdfDictionary>(PdfNames.CharProcs) is { } glyphs)
                        {
                            var glyphScope = scope.Enter(font.GetResolved(PdfNames.Resources) as PdfDictionary, where: null);
                            foreach (var (_, glyph) in glyphs)
                            {
                                if (PdfReference.ResolveIfReference(glyph) is PdfStream procedure)
                                {
                                    ReadContent(procedure, glyphScope, depth + 1);
                                }
                            }
                        }

                        break;
                }
            }
        }

        var pageNumber = 0;
        foreach (var page in document.Pages)
        {
            pageNumber++;
            var scope = ColorScope.Root(page.Resources, $"page {pageNumber}");
            if (page.Dictionary.Get<PdfDictionary>(PdfNames.Group) is { } group)
            {
                UseColorSpace(group[PdfNames.CS], scope, depth: 0);
            }

            foreach (var content in page.Dictionary.GetResolved(PdfNames.Contents) switch
            {
                PdfStream stream => [stream],
                PdfArray array => array.Select(PdfReference.ResolveIfReference).OfType<PdfStream>().ToArray(),
                _ => [],
            })
            {
                ReadContent(content, scope, depth: 0);
            }

            foreach (var annotation in page.Annotations)
            {
                // The appearances do not use the resources of the page
                var annotationScope = ColorScope.Root(resources: null, $"the {annotation.Subtype} annotation of page {pageNumber}");
                foreach (var appearance in GetAppearanceStreams(annotation))
                {
                    UseForm(appearance, annotationScope, where: null, depth: 0);
                }
            }
        }
    }

    /// <summary>
    /// Checks that the document information can be written to the XMP metadata, which PDF/A-1, PDF/A-2 and PDF/A-3
    /// need to agree with it: XML does not allow the control characters and the lone surrogates.
    /// </summary>
    private static void CheckInformationIsXmlText(PdfDocument document, int part, List<string> errors)
    {
        var information = document.Information;
        foreach (var (key, value) in new[] { ("Title", information.Title), ("Author", information.Author), ("Subject", information.Subject), ("Keywords", information.Keywords), ("Creator", information.Creator), ("Producer", information.Producer) })
        {
            if (value is not null && !ReferenceEquals(XmpPacketWriter.Sanitize(value), value))
            {
                errors.Add($"PDF/A-{part} needs the document information and the XMP metadata to agree: the {key} has a character that XML does not allow (a control character or a lone surrogate)");
            }
        }
    }

    /// <summary>The resources a content stream uses, and the default color spaces that apply to it.</summary>
    private sealed class ColorScope
    {
        private ColorScope(PdfDictionary? resources, bool hasDefaultRgb, bool hasDefaultCmyk, string where)
        {
            Resources = resources;
            HasDefaultRgb = hasDefaultRgb;
            HasDefaultCmyk = hasDefaultCmyk;
            Where = where;
        }

        public PdfDictionary? Resources { get; }

        public bool HasDefaultRgb { get; }

        public bool HasDefaultCmyk { get; }

        /// <summary>What uses the color spaces, for the messages.</summary>
        public string Where { get; }

        public static ColorScope Root(PdfDictionary? resources, string where) => new(resources, HasDefault(resources, DefaultRgbName), HasDefault(resources, DefaultCmykName), where);

        /// <summary>The scope of a form, a pattern or a glyph: its resources, or the current ones when it has none.</summary>
        public ColorScope Enter(PdfDictionary? resources, string? where)
        {
            var current = resources ?? Resources;
            return new(current, HasDefaultRgb || HasDefault(current, DefaultRgbName), HasDefaultCmyk || HasDefault(current, DefaultCmykName), where ?? Where);
        }

        private static bool HasDefault(PdfDictionary? resources, PdfName name) => resources?.Get<PdfDictionary>(PdfNames.ColorSpace)?.ContainsKey(name) is true;
    }
}
