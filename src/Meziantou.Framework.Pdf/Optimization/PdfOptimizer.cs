using System.Globalization;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Optimization;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Makes documents smaller or removes what they do not need, in two steps: <see cref="CreatePlan"/> lists the changes
/// without making them, and <see cref="Apply"/> makes the changes of the plan that are enabled.
/// </summary>
/// <remarks>
/// The changes are made to the document model: save the document to write them. A full save writes only the objects
/// reachable from the trailer, so the removed objects are not written.
/// </remarks>
public static class PdfOptimizer
{
    private const int MinCompressedLength = 32;
    private const int MaxActionDepth = 64;

    private static readonly PdfName NamesName = PdfName.Create("Names");
    private static readonly PdfName JavaScriptName = PdfName.Create("JavaScript");
    private static readonly PdfName EmbeddedFilesName = PdfName.Create("EmbeddedFiles");
    private static readonly PdfName AssociatedFilesName = PdfName.Create("AF");
    private static readonly PdfName AdditionalActionsName = PdfName.Create("AA");
    private static readonly PdfName ActionName = PdfName.Create("A");
    private static readonly PdfName NextName = PdfName.Create("Next");
    private static readonly PdfName ThumbName = PdfName.Create("Thumb");
    private static readonly PdfName OpenActionName = PdfName.Create("OpenAction");
    private static readonly PdfName AcroFormName = PdfName.Create("AcroForm");
    private static readonly PdfName XfaName = PdfName.Create("XFA");
    private static readonly PdfName NeedsRenderingName = PdfName.Create("NeedsRendering");
    private static readonly PdfName OutlinesName = PdfName.Create("Outlines");
    private static readonly PdfName PageModeName = PdfName.Create("PageMode");
    private static readonly PdfName FieldsName = PdfName.Create("Fields");
    private static readonly PdfName ObjStmName = PdfName.Create("ObjStm");
    private static readonly PdfName XRefName = PdfName.Create("XRef");
    private static readonly PdfName LinearizedName = PdfName.Create("Linearized");
    private static readonly PdfName MaskName = PdfName.Create("Mask");
    private static readonly PdfName ImageMaskName = PdfName.Create("ImageMask");
    private static readonly PdfName DecodeName = PdfName.Create("Decode");
    private static readonly PdfName SMaskInDataName = PdfName.Create("SMaskInData");
    private static readonly PdfName IntentName = PdfName.Create("Intent");
    private static readonly PdfName FieldNameKey = PdfName.Create("T");

    /// <summary>The markup annotations (12.5.6.2, table 171) and the pop-ups that show their text.</summary>
    private static readonly HashSet<string> CommentSubtypes = new(StringComparer.Ordinal)
    {
        "Text", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly",
        "StrikeOut", "Caret", "Stamp", "Ink", "Sound", "Redact", "Popup",
    };

    /// <summary>The entries of an image kept when it is resampled.</summary>
    private static readonly PdfName[] KeptImageEntries = [PdfNames.Interpolate, IntentName, PdfName.Create("OC"), PdfNames.StructParent, PdfNames.Metadata, PdfName.Create("Alt"), PdfName.Create("Name")];

    /// <summary>Lists the changes the options ask for, without changing the document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The changes to plan.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="PdfException">The document cannot be read.</exception>
    public static PdfOptimizationPlan CreatePlan(PdfDocument document, PdfOptimizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var actions = new List<PdfOptimizationAction>();
        var catalog = document.Catalog;
        if (options.RemoveMetadata)
        {
            PlanMetadata(document, actions);
        }

        if (options.RemoveJavaScript)
        {
            PlanJavaScript(document, actions);
        }

        if (options.RemoveAttachments)
        {
            PlanAttachments(document, actions);
        }

        if (options.RemovePageThumbnails)
        {
            ForEachPage(document, (page, index) =>
            {
                if (page.Dictionary.ContainsKey(ThumbName))
                {
                    actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemovePageThumbnail, $"Remove the thumbnail of the page {index + 1}", () => page.Dictionary.Remove(ThumbName), index));
                }
            });
        }

        if (options.RemoveComments)
        {
            PlanAnnotations(document, actions, CommentSubtypes, PdfOptimizationActionKind.RemoveComment, "comment");
        }

        if (options.RemoveOpenAction && catalog.ContainsKey(OpenActionName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveOpenAction, "Remove the open action of the document", () => catalog.Remove(OpenActionName)));
        }

        if (options.RemoveXfa && catalog.GetResolved(AcroFormName) is PdfDictionary acroForm && acroForm.ContainsKey(XfaName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveXfa, "Remove the XFA form", () =>
            {
                catalog.Remove(NeedsRenderingName);
                return acroForm.Remove(XfaName);
            }));
        }

        if (options.RemoveFormFields && catalog.ContainsKey(AcroFormName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveFormFields, "Remove the interactive form and the widgets of its fields", () => RemoveFormFields(document)));
        }

        if (options.RemoveBookmarks && catalog.ContainsKey(OutlinesName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveBookmarks, "Remove the outline (bookmarks)", () =>
            {
                document.Outlines.Clear();
                if (catalog.GetName(PageModeName)?.Value == "UseOutlines")
                {
                    catalog.Remove(PageModeName);
                }

                return catalog.Remove(OutlinesName);
            }));
        }

        if (options.FlattenOptionalContent && OptionalContentFlattener.HasOptionalContent(document))
        {
            var flattener = new OptionalContentFlattener(document);
            var pageActions = new List<PdfOptimizationAction>();
            ForEachPage(document, (page, index) => pageActions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.FlattenOptionalContent, $"Flatten the optional content of the page {index + 1}", () => flattener.FlattenPage(page), index)));
            actions.AddRange(pageActions);
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveOptionalContentProperties, "Remove the optional content properties (layers) of the document, when the optional content of every page was flattened", () =>
            {
                // A page left unflattened still refers to its groups: without the properties, its hidden content would show
                if (flattener.IsIncomplete || !pageActions.TrueForAll(action => action.IsEnabled))
                    return false;

                OptionalContentFlattener.RemoveProperties(document);
                return true;
            }));
        }

        ObjectGraph? graph = null;
        if (options.PruneUnusedResources)
        {
            graph ??= ObjectGraph.Build(document);
            PlanUnusedResources(document, graph, actions);
        }

        if (options.MaxImageResolution is { } resolution)
        {
            PlanImages(document, resolution, actions);
        }

        if (options.CompressStreams)
        {
            graph ??= ObjectGraph.Build(document);
            PlanCompression(graph, actions);
        }

        if (options.PruneUnreachableObjects)
        {
            // Last: the objects the other changes leave unreachable are found when the plan is applied
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.PruneUnreachableObject, "Remove the objects that cannot be reached from the trailer", () => PruneUnreachableObjects(document)));
        }

        return new PdfOptimizationPlan(document, options, actions, HasSignatures(document));
    }

    /// <summary>Makes the enabled changes of a plan.</summary>
    /// <param name="plan">The plan, from <see cref="CreatePlan"/>.</param>
    /// <returns>The actions that changed the document; an action whose target was already removed (by another action, or since the plan was created) does not.</returns>
    /// <exception cref="InvalidOperationException">The plan invalidates signatures, and <see cref="PdfOptimizationOptions.AllowSignatureInvalidation"/> is <see langword="false"/>.</exception>
    /// <exception cref="PdfException">The document cannot be read.</exception>
    public static IReadOnlyList<PdfOptimizationAction> Apply(PdfOptimizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.InvalidatesSignatures && !plan.Options.AllowSignatureInvalidation)
            throw new InvalidOperationException("The document is signed, and the plan changes it: the signatures would no longer be valid. Set AllowSignatureInvalidation to apply it anyway.");

        var applied = new List<PdfOptimizationAction>();
        foreach (var action in plan.Actions)
        {
            if (action.IsEnabled && action.Apply())
            {
                applied.Add(action);
            }
        }

        return applied;
    }

    private static void ForEachPage(PdfDocument document, Action<PdfPage, int> action)
    {
        var index = 0;
        foreach (var page in document.Pages)
        {
            action(page, index);
            index++;
        }
    }

    private static bool HasSignatures(PdfDocument document)
    {
        try
        {
            return document.Form.Exists && document.Form.GetFields().OfType<PdfSignatureField>().Any(field => field.IsSigned);
        }
        catch (PdfException)
        {
            return false;
        }
    }

    private static void PlanMetadata(PdfDocument document, List<PdfOptimizationAction> actions)
    {
        if (document.Trailer.ContainsKey(PdfNames.Info))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveDocumentInformation, "Remove the document information dictionary", () => document.Trailer.Remove(PdfNames.Info), objectNumber: (document.Trailer[PdfNames.Info] as PdfReference)?.ObjectNumber));
        }

        if (document.Catalog.ContainsKey(PdfNames.Metadata))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveXmpMetadata, "Remove the XMP metadata of the document", () =>
            {
                if (!document.Catalog.ContainsKey(PdfNames.Metadata))
                    return false;

                document.RemoveXmpMetadata();
                return true;
            }, objectNumber: (document.Catalog[PdfNames.Metadata] as PdfReference)?.ObjectNumber));
        }
    }

    private static void PlanJavaScript(PdfDocument document, List<PdfOptimizationAction> actions)
    {
        var catalog = document.Catalog;
        if (catalog.GetResolved(NamesName) is PdfDictionary names && names.ContainsKey(JavaScriptName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveJavaScript, "Remove the scripts of the document (JavaScript name tree)", () => names.Remove(JavaScriptName)));
        }

        PlanActions(catalog, "the document", pageIndex: null, actions);
        if (IsJavaScriptAction(catalog.GetResolved(OpenActionName), depth: 0))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveJavaScript, "Remove the JavaScript open action of the document", () => catalog.Remove(OpenActionName)));
        }

        ForEachPage(document, (page, index) =>
        {
            PlanActions(page.Dictionary, $"the page {index + 1}", index, actions);
            foreach (var annotation in page.Annotations)
            {
                PlanActions(annotation.Dictionary, $"a {annotation.Subtype} annotation of the page {index + 1}", index, actions);
            }
        });

        // The fields that are not merged with their widget
        if (catalog.GetResolved(AcroFormName) is PdfDictionary form && form.GetResolved(FieldsName) is PdfArray fields)
        {
            var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<(PdfArray Fields, int Depth)>();
            pending.Push((fields, 0));
            while (pending.TryPop(out var item))
            {
                for (var i = 0; i < item.Fields.Count; i++)
                {
                    if (!item.Fields.TryGetResolved<PdfDictionary>(i, out var field) || !visited.Add(field) || field.IsType(PdfNames.Annot) || field.ContainsKey(PdfNames.Subtype))
                        continue;

                    PlanActions(field, $"the field {field.GetText(FieldNameKey)}", pageIndex: null, actions);
                    if (item.Depth < MaxActionDepth && field.GetResolved(PdfNames.Kids) is PdfArray kids)
                    {
                        pending.Push((kids, item.Depth + 1));
                    }
                }
            }
        }
    }

    /// <summary>Plans the removal of the JavaScript actions of a dictionary: its action (<c>/A</c>) and its additional actions (<c>/AA</c>, 12.6.3).</summary>
    private static void PlanActions(PdfDictionary owner, string description, int? pageIndex, List<PdfOptimizationAction> actions)
    {
        if (IsJavaScriptAction(owner.GetResolved(ActionName), depth: 0))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveJavaScript, $"Remove the JavaScript action of {description}", () => owner.Remove(ActionName), pageIndex));
        }

        if (owner.GetResolved(AdditionalActionsName) is PdfDictionary additional)
        {
            foreach (var trigger in additional.Keys.ToList())
            {
                if (IsJavaScriptAction(additional.GetResolved(trigger), depth: 0))
                {
                    actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveJavaScript, $"Remove the JavaScript action /{trigger.Value} of {description}", () =>
                    {
                        var removed = additional.Remove(trigger);
                        if (additional.Count == 0)
                        {
                            owner.Remove(AdditionalActionsName);
                        }

                        return removed;
                    }, pageIndex));
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether an action runs JavaScript, itself or in the actions that follow it (<c>/Next</c>, 12.6.2).</summary>
    private static bool IsJavaScriptAction(PdfObject? value, int depth)
    {
        if (depth > MaxActionDepth)
            return false;

        switch (value)
        {
            case PdfDictionary action:
                if (action.GetName(PdfNames.S)?.Value == "JavaScript")
                    return true;

                return IsJavaScriptAction(action.GetResolved(NextName), depth + 1);

            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (IsJavaScriptAction(array.GetResolved(i), depth + 1))
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static void PlanAttachments(PdfDocument document, List<PdfOptimizationAction> actions)
    {
        var catalog = document.Catalog;
        if (catalog.GetResolved(NamesName) is PdfDictionary names && names.ContainsKey(EmbeddedFilesName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveEmbeddedFiles, "Remove the embedded files of the document", () =>
            {
                catalog.Remove(AssociatedFilesName);
                return names.Remove(EmbeddedFilesName);
            }));
        }
        else if (catalog.ContainsKey(AssociatedFilesName))
        {
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.RemoveEmbeddedFiles, "Remove the files associated with the document", () => catalog.Remove(AssociatedFilesName)));
        }

        PlanAnnotations(document, actions, new HashSet<string>(StringComparer.Ordinal) { "FileAttachment" }, PdfOptimizationActionKind.RemoveFileAttachmentAnnotation, "file attachment");
    }

    private static void PlanAnnotations(PdfDocument document, List<PdfOptimizationAction> actions, HashSet<string> subtypes, PdfOptimizationActionKind kind, string description)
    {
        ForEachPage(document, (page, index) =>
        {
            foreach (var annotation in page.Annotations)
            {
                if (!subtypes.Contains(annotation.Subtype))
                    continue;

                var text = annotation.Subtype == "Popup" ? "pop-up" : $"{description} ({annotation.Subtype})";
                actions.Add(new PdfOptimizationAction(kind, $"Remove a {text} annotation of the page {index + 1}", () => page.Annotations.Remove(annotation), index, annotation.Reference?.ObjectNumber));
            }
        });
    }

    private static bool RemoveFormFields(PdfDocument document)
    {
        if (!document.Catalog.Remove(AcroFormName))
            return false;

        foreach (var page in document.Pages)
        {
            foreach (var annotation in page.Annotations.Where(annotation => annotation.Subtype == "Widget").ToList())
            {
                page.Annotations.Remove(annotation);
            }
        }

        return true;
    }

    /// <summary>
    /// Plans the removal of the resources the pages do not use. A dictionary of resources (<c>/Font</c>, <c>/XObject</c>...)
    /// is pruned only when it belongs to the resources of pages alone: a dictionary shared with a form, a pattern or an
    /// annotation is kept as it is. The names it keeps are those the content of any of its pages uses.
    /// </summary>
    private static void PlanUnusedResources(PdfDocument document, ObjectGraph graph, List<PdfOptimizationAction> actions)
    {
        var used = new Dictionary<PdfDictionary, (HashSet<PdfName> Names, PdfName Category, List<int> Pages, bool Unknown)>(ReferenceEqualityComparer.Instance);
        var pageResources = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        ForEachPage(document, (page, index) =>
        {
            if (page.Resources is not { } resources)
                return;

            pageResources.Add(resources);
            var names = new HashSet<(PdfName Category, PdfName Name)>();
            bool known;
            try
            {
                known = ContentResourceUsage.TryCollect(page.GetContentData(), resources, names);
            }
            catch (PdfException)
            {
                known = false;
            }

            foreach (var (category, value) in resources)
            {
                if (resources.GetResolved(category) is not PdfDictionary dictionary)
                    continue;

                if (!used.TryGetValue(dictionary, out var entry))
                {
                    entry = ([], category, [], false);
                }

                entry.Pages.Add(index);
                entry.Unknown |= !known || entry.Category != category;
                entry.Names.UnionWith(names.Where(name => name.Category == category).Select(name => name.Name));
                used[dictionary] = entry;
            }
        });

        foreach (var (dictionary, entry) in used)
        {
            if (entry.Unknown || !IsOwnedByPages(dictionary, pageResources, graph))
                continue;

            var pages = string.Join(", ", entry.Pages.Select(page => (page + 1).ToString(CultureInfo.InvariantCulture)));
            foreach (var name in dictionary.Keys.ToList())
            {
                if (entry.Names.Contains(name))
                    continue;

                var category = entry.Category.Value;
                actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.PruneUnusedResource, $"Remove the unused {category} resource /{name.Value} (pages {pages})", () => dictionary.Remove(name), entry.Pages[0], (dictionary[name] as PdfReference)?.ObjectNumber, name.Value));
            }
        }
    }

    /// <summary>Gets a value indicating whether a dictionary of resources is held by the resources of pages only, whose own holders are pages or nodes of the page tree.</summary>
    private static bool IsOwnedByPages(PdfDictionary dictionary, HashSet<PdfObject> pageResources, ObjectGraph graph)
    {
        var containers = graph.GetContainers(dictionary);
        if (containers.Count == 0)
            return false;

        foreach (var container in containers)
        {
            if (!pageResources.Contains(container))
                return false;

            foreach (var owner in graph.GetContainers(container))
            {
                if (owner is not PdfDictionary node || !(node.IsType(PdfNames.Page) || node.IsType(PdfNames.Pages)))
                    return false;
            }
        }

        return true;
    }

    private static void PlanImages(PdfDocument document, double maxResolution, List<PdfOptimizationAction> actions)
    {
        var images = new Dictionary<PdfReference, (PdfImageOccurrence Occurrence, double Resolution)>();
        foreach (var page in document.Pages)
        {
            IReadOnlyList<PdfImageOccurrence> occurrences;
            try
            {
                occurrences = page.GetImages();
            }
            catch (PdfException)
            {
                continue;
            }

            foreach (var occurrence in occurrences)
            {
                if (occurrence.IsInline || occurrence.Reference is not { } reference)
                    continue;

                // The lengths of the sides of the unit square of the image on the page, in points (8.9.4)
                var matrix = occurrence.Matrix;
                var width = Math.Sqrt((matrix.A * matrix.A) + (matrix.B * matrix.B)) * page.UserUnit;
                var height = Math.Sqrt((matrix.C * matrix.C) + (matrix.D * matrix.D)) * page.UserUnit;
                if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height))
                    continue;

                var resolution = Math.Max(occurrence.Width * 72 / width, occurrence.Height * 72 / height);
                if (!images.TryGetValue(reference, out var current) || resolution > current.Resolution)
                {
                    images[reference] = (occurrence, resolution);
                }
            }
        }

        foreach (var (reference, (occurrence, resolution)) in images.OrderBy(item => item.Key.ObjectNumber))
        {
            if (resolution <= maxResolution * 1.01 || !CanResample(occurrence))
                continue;

            var scale = maxResolution / resolution;
            var newWidth = Math.Max(1, (int)Math.Round(occurrence.Width * scale, MidpointRounding.AwayFromZero));
            var newHeight = Math.Max(1, (int)Math.Round(occurrence.Height * scale, MidpointRounding.AwayFromZero));
            if (newWidth >= occurrence.Width && newHeight >= occurrence.Height)
                continue;

            var description = string.Create(CultureInfo.InvariantCulture, $"Resample the image {reference.ObjectNumber} from {resolution:0} to {maxResolution:0} ppi ({occurrence.Width} x {occurrence.Height} to {newWidth} x {newHeight} pixels)");
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.DownsampleImage, description, () => Resample(document, reference, occurrence, newWidth, newHeight), document.Pages.IndexOf(occurrence.Page), reference.ObjectNumber));
        }
    }

    private static bool CanResample(PdfImageOccurrence occurrence)
    {
        var dictionary = occurrence.Dictionary;
        if (occurrence.BitsPerComponent != 8 || occurrence.IsImageMask || dictionary.ContainsKey(MaskName) || dictionary.ContainsKey(DecodeName) || dictionary.ContainsKey(ImageMaskName) || dictionary.GetInteger(SMaskInDataName) != 0)
            return false;

        foreach (var filter in occurrence.Filters)
        {
            if (filter.Value is not ("FlateDecode" or "Fl" or "DCTDecode" or "DCT" or "LZWDecode" or "LZW"))
                return false;
        }

        return occurrence.ColorSpace switch
        {
            PdfName name => name.Value is "DeviceRGB" or "DeviceGray" or "RGB" or "G",
            PdfArray { Count: 2 } array when array.GetResolved(0) is PdfName { Value: "ICCBased" } && array.TryGetResolved<PdfStream>(1, out var profile) => profile.Dictionary.GetInteger(PdfNames.N) is 1 or 3,
            _ => false,
        };
    }

    private static bool Resample(PdfDocument document, PdfReference reference, PdfImageOccurrence occurrence, int width, int height)
    {
        if (document.Objects.Resolve(reference) is not PdfStream original || !ReferenceEquals(original.Dictionary, occurrence.Dictionary))
            return false;

        Image<Rgba32>? decoded;
        try
        {
            decoded = occurrence.Decode();
        }
        catch (PdfException)
        {
            return false;
        }

        if (decoded is null)
            return false;

        using (decoded)
        {
            decoded.Resize(new ResizeOptions(width, height) { Mode = ResizeMode.Stretch });
            var isGray = occurrence.ColorSpace is PdfName { Value: "DeviceGray" or "G" } || (occurrence.ColorSpace is PdfArray array && array.TryGetResolved<PdfStream>(1, out var profile) && profile.Dictionary.GetInteger(PdfNames.N) == 1);
            using var gray = isGray && !HasTransparency(decoded) ? decoded.CloneAs<Gray8>() : null;
            Image source = gray ?? (Image)decoded;
            var isJpeg = occurrence.Filters.Any(filter => filter.Value is "DCTDecode" or "DCT");
            var image = isJpeg ? PdfImage.FromImageAsJpeg(source) : PdfImage.FromImage(source);
            var created = image.GetReference(document);
            var stream = (PdfStream)document.Objects.Resolve(created);
            foreach (var key in KeptImageEntries)
            {
                if (original.Dictionary[key] is { } value)
                {
                    stream.Dictionary[key] = value;
                }
            }

            document.Objects.Remove(created);
            document.Objects.Set(reference, stream);
        }

        return true;
    }

    private static bool HasTransparency(Image<Rgba32> image)
    {
        var pixels = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels.Any(pixel => pixel.A != 255);
    }

    private static void PlanCompression(ObjectGraph graph, List<PdfOptimizationAction> actions)
    {
        foreach (var (stream, reference) in graph.Streams)
        {
            var dictionary = stream.Dictionary;
            if (dictionary.ContainsKey(PdfNames.Filter) || dictionary.ContainsKey(PdfNames.DecodeParms) || dictionary.IsType(PdfNames.Metadata) || dictionary.IsType(XRefName) || dictionary.IsType(ObjStmName))
                continue;

            int length;
            try
            {
                length = stream.EncodedData.Length;
            }
            catch (PdfException)
            {
                continue;
            }

            if (length < MinCompressedLength)
                continue;

            var name = reference is null ? "a direct stream" : $"the stream {reference.ObjectNumber}";
            actions.Add(new PdfOptimizationAction(PdfOptimizationActionKind.CompressStream, $"Compress {name} ({length} bytes) with FlateDecode", () =>
            {
                if (stream.Dictionary.ContainsKey(PdfNames.Filter))
                    return false;

                var data = stream.EncodedData.Span;
                var compressed = FlateFilter.Encode(data);
                if (compressed.Length >= data.Length)
                    return false;

                stream.SetEncodedData(compressed, PdfNames.FlateDecode);
                return true;
            }, objectNumber: reference?.ObjectNumber));
        }
    }

    /// <summary>Removes the indirect objects that cannot be reached from the trailer; the object streams and the cross-reference streams of the file, which hold its other objects, are kept.</summary>
    private static bool PruneUnreachableObjects(PdfDocument document)
    {
        var graph = ObjectGraph.Build(document);
        var removed = false;
        foreach (var reference in document.Objects.GetReferences().ToList())
        {
            if (graph.ReachedObjectNumbers.Contains(reference.ObjectNumber))
                continue;

            PdfObject value;
            try
            {
                value = document.Objects.Resolve(reference);
            }
            catch (PdfException)
            {
                continue;
            }

            var dictionary = value switch
            {
                PdfStream stream => stream.Dictionary,
                PdfDictionary d => d,
                _ => null,
            };
            if (dictionary is not null && (dictionary.IsType(ObjStmName) || dictionary.IsType(XRefName) || dictionary.ContainsKey(LinearizedName)))
                continue;

            removed |= document.Objects.Remove(reference);
        }

        return removed;
    }
}
