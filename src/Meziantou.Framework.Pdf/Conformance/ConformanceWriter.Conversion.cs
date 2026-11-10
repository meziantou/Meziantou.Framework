using Meziantou.Framework.Imaging.Metadata;
using System.Text;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Metadata;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Graphics;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Conformance;

/// <summary>
/// The conversion of an existing document to PDF/A-2 or PDF/A-3 (levels b and u): it removes what PDF/A does not allow
/// and completes what it needs, without moving anything on the pages, then checks the document as the save does.
/// </summary>
internal static partial class ConformanceWriter
{
    private static readonly string[] AllowedActions = ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"];
    private static readonly string[] ForbiddenAnnotations = ["3D", "Sound", "Screen", "Movie", "RichMedia"];
    private static readonly PdfName XfaName = PdfName.Create("XFA");
    private static readonly PdfName NeedsRenderingName = PdfName.Create("NeedsRendering");
    private static readonly PdfName AlternatesName = PdfName.Create("Alternates");
    private static readonly PdfName OpiName = PdfName.Create("OPI");
    private static readonly PdfName RefName = PdfName.Create("Ref");
    private static readonly PdfName PSName = PdfName.Create("PS");
    private static readonly PdfName DefaultName = PdfName.Create("Default");
    private static readonly PdfName ConfigsName = PdfName.Create("Configs");
    private static readonly PdfName ASName = PdfName.Create("AS");
    private static readonly PdfName UnspecifiedName = PdfName.Create("Unspecified");
    private static readonly PdfName OctetStreamName = PdfName.Create("application/octet-stream");
    private static readonly PdfName ICCBasedName = PdfName.Create("ICCBased");

    public static PdfAConversionResult ConvertToPdfA(PdfDocument document, PdfAConformance conformance, PdfAConversionOptions options)
    {
        var part = PdfALevel.GetPart(conformance);
        var changes = new List<string>();
        var violations = new List<string>();
        void Change(string text)
        {
            if (!changes.Contains(text, StringComparer.Ordinal))
            {
                changes.Add(text);
            }
        }

        if (document.Encryption is not null)
        {
            document.Encryption = null;
            Change("Removed the encryption");
        }

        if (document.Version > PdfVersion.Pdf17)
        {
            document.Version = PdfVersion.Pdf17;
            document.Catalog.Remove(PdfNames.Version);
            Change("Set the version of the document to PDF 1.7");
        }

        RemoveForbiddenActions(document, Change);
        RemoveDynamicForms(document, Change);
        GenerateFieldAppearances(document, Change);
        FixAnnotations(document, Change, violations);
        FixEmbeddedFiles(document, part, Change);
        ReencodeLzwStreams(document, Change, violations);
        FixInlineImages(document, Change, violations);
        FixResources(document, Change);
        FixOptionalContent(document, Change);
        EmbedSubstituteFonts(document, Change);
        FixColorSpaces(document, part, options, Change, violations);
        FixInformation(document, Change);
        if (!options.KeepXmpMetadata && document.Catalog.ContainsKey(PdfNames.Metadata))
        {
            document.Catalog.Remove(PdfNames.Metadata);
            Change("Removed the XMP metadata: the save writes it again from the document information");
        }

        if (conformance is PdfAConformance.PdfA2U or PdfAConformance.PdfA3U)
        {
            var pageNumber = 0;
            foreach (var page in document.Pages)
            {
                pageNumber++;
                if (TextExtractor.CountGlyphsWithoutText(page) > 0)
                {
                    violations.Add($"Page {pageNumber} shows glyphs whose fonts give no Unicode text: the level u needs the text of every glyph (use the level b)");
                }
            }
        }

        // The checks of the save: what the fixes did not solve
        var previous = document.PdfAConformance;
        document.PdfAConformance = conformance;
        foreach (var error in Validate(document))
        {
            if (!violations.Contains(error, StringComparer.Ordinal))
            {
                violations.Add(error);
            }
        }

        if (violations.Count > 0)
        {
            document.PdfAConformance = previous;
        }

        return new PdfAConversionResult(changes, violations);
    }

    /// <summary>
    /// Removes the actions PDF/A-2 and PDF/A-3 do not allow (ISO 19005-2, 6.5 and 6.4.1): the types other than GoTo,
    /// GoToR, GoToE, Thread, URI, Named (NextPage, PrevPage, FirstPage, LastPage) and SubmitForm, an action whose next
    /// actions have one, the JavaScript of the document, the actions of the widgets and every additional action.
    /// </summary>
    private static void RemoveForbiddenActions(PdfDocument document, Action<string> change)
    {
        bool IsAllowed(PdfObject? value, int depth, out string? type)
        {
            type = null;
            if (depth > MaxDepth)
                return false;

            switch (Resolve(value))
            {
                case PdfDictionary action:
                    type = action.GetName(PdfNames.S)?.Value;
                    if (type is not null && !AllowedActions.Contains(type, StringComparer.Ordinal))
                        return false;

                    if (type == "Named" && action.GetName(PdfNames.N)?.Value is not ("NextPage" or "PrevPage" or "FirstPage" or "LastPage"))
                        return false;

                    return action[PdfNames.Next] is null || IsAllowed(action[PdfNames.Next], depth + 1, out type);
                case PdfArray actions:
                    foreach (var item in actions)
                    {
                        if (!IsAllowed(item, depth + 1, out type))
                            return false;
                    }

                    return true;
                default:
                    return true;
            }
        }

        void RemoveAction(PdfDictionary owner, PdfName key)
        {
            if (owner[key] is { } action && !IsAllowed(action, depth: 0, out var type))
            {
                owner.Remove(key);
                change($"Removed the {type ?? "unknown"} actions");
            }
        }

        void RemoveAdditionalActions(PdfDictionary owner)
        {
            if (owner.Remove(AdditionalActionsName))
            {
                change("Removed the additional actions (AA)");
            }
        }

        var catalog = document.Catalog;
        if (catalog.GetResolved(OpenActionName) is PdfDictionary)
        {
            RemoveAction(catalog, OpenActionName);
        }

        RemoveAdditionalActions(catalog);
        if (catalog.Get<PdfDictionary>(PdfNames.Names) is { } names && names.Remove(JavaScriptName))
        {
            change("Removed the JavaScript of the document");
        }

        foreach (var page in document.Pages)
        {
            RemoveAdditionalActions(page.Dictionary);
            foreach (var annotation in page.Annotations)
            {
                if (annotation.Subtype == "Widget")
                {
                    if (annotation.Dictionary.Remove(PdfNames.A))
                    {
                        change("Removed the actions of the widget annotations");
                    }
                }
                else
                {
                    RemoveAction(annotation.Dictionary, PdfNames.A);
                }

                RemoveAdditionalActions(annotation.Dictionary);
            }
        }

        foreach (var (item, _) in EnumerateOutlineItems(document))
        {
            RemoveAction(item, PdfNames.A);
        }

        foreach (var field in EnumerateFields(document))
        {
            RemoveAdditionalActions(field);
        }
    }

    /// <summary>Removes the XFA form and the NeedsRendering flag (ISO 19005-2, 6.4.2): PDF/A keeps the interactive form only.</summary>
    private static void RemoveDynamicForms(PdfDocument document, Action<string> change)
    {
        if (document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)?.Remove(XfaName) is true)
        {
            change("Removed the XFA form");
        }

        if (document.Catalog.Remove(NeedsRenderingName))
        {
            change("Removed the NeedsRendering flag");
        }
    }

    /// <summary>Writes the appearances of the text and choice fields when the form asks the viewer to make them (<c>/NeedAppearances</c>), then clears the flag.</summary>
    private static void GenerateFieldAppearances(PdfDocument document, Action<string> change)
    {
        if (document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm) is not { } form || form.Get<PdfBoolean>(NeedAppearancesName)?.Value is not true)
            return;

        foreach (var field in document.Form.GetFields())
        {
            if (field is PdfTextField or PdfChoiceField)
            {
                PdfFieldAppearance.Update(field);
            }
        }

        form.Remove(NeedAppearancesName);
        change("Generated the appearances of the fields, and removed the NeedAppearances flag");
    }

    /// <summary>
    /// Removes the annotations PDF/A-2 does not allow (3D, Sound, Screen, Movie, RichMedia), keeps the normal appearance
    /// only, and generates the appearance of the annotations that have none (ISO 19005-2, 6.3).
    /// </summary>
    private static void FixAnnotations(PdfDocument document, Action<string> change, List<string> violations)
    {
        var pageNumber = 0;
        foreach (var page in document.Pages)
        {
            pageNumber++;
            foreach (var annotation in page.Annotations.ToList())
            {
                var dictionary = annotation.Dictionary;
                var subtype = annotation.Subtype;
                if (ForbiddenAnnotations.Contains(subtype, StringComparer.Ordinal))
                {
                    page.Annotations.Remove(annotation);
                    change($"Removed the {subtype} annotations");
                    continue;
                }

                if (dictionary.GetResolved(AppearanceName) is PdfDictionary appearances)
                {
                    foreach (var key in appearances.Select(entry => entry.Key).Where(key => key != PdfNames.N).ToList())
                    {
                        appearances.Remove(key);
                        change("Removed the rollover and down appearances of the annotations");
                    }

                    continue;
                }

                if (subtype is "Popup" or "Link" || HasNoSize(dictionary))
                    continue;

                if (PdfRectangle.TryRead(dictionary.Get<PdfArray>(PdfNames.Rect), out var rectangle) && AppearanceGenerator.Generate(page, dictionary, rectangle) is { } appearance)
                {
                    dictionary[AppearanceName] = new PdfDictionary { [PdfNames.N] = document.Objects.Add(appearance) };
                    change($"Generated the appearance of the {subtype} annotations that had none");
                }
                else
                {
                    violations.Add($"The {subtype} annotation of page {pageNumber} has no appearance, and the library cannot generate one: remove it");
                }
            }
        }
    }

    /// <summary>
    /// Makes the embedded files meet PDF/A-2 (ISO 19005-2, 6.8: PDF/A documents only, the others are removed) or
    /// PDF/A-3 (ISO 19005-3, 6.8: a name, a MIME type and a relationship, and an association with the document).
    /// </summary>
    private static void FixEmbeddedFiles(PdfDocument document, int part, Action<string> change)
    {
        var files = GetEmbeddedFiles(document);
        if (files.Count == 0)
            return;

        if (part == 2)
        {
            foreach (var file in files)
            {
                if (GetPdfAPart(file) is 1 or 2)
                    continue;

                document.EmbeddedFiles.Remove(file);
                foreach (var page in document.Pages)
                {
                    foreach (var annotation in page.Annotations.ToList())
                    {
                        if (annotation is PdfFileAttachmentAnnotation { File: { } attached } && ReferenceEquals(attached.Dictionary, file.Dictionary))
                        {
                            page.Annotations.Remove(annotation);
                        }
                    }
                }

                change($"Removed the embedded file '{file.FileName}': PDF/A-2 embeds PDF/A documents only (use PDF/A-3)");
            }

            return;
        }

        var associated = document.Catalog.Get<PdfArray>(PdfEmbeddedFile.AFName);
        foreach (var file in files)
        {
            var name = file.FileName;
            if (file.Dictionary.GetText(PdfNames.F) is null || file.Dictionary.GetText(PdfEmbeddedFile.UFName) is null)
            {
                file.Dictionary[PdfNames.F] ??= PdfString.FromText(name);
                file.Dictionary[PdfEmbeddedFile.UFName] ??= PdfString.FromText(name);
                change("Completed the names of the embedded files (F and UF)");
            }

            if (file.MimeType is null && file.Stream is { } stream)
            {
                stream.Dictionary[PdfNames.Subtype] = OctetStreamName;
                change("Set the MIME type of the embedded files that had none to application/octet-stream");
            }

            if (file.Dictionary.GetName(PdfEmbeddedFile.AFRelationshipName) is null)
            {
                file.Dictionary[PdfEmbeddedFile.AFRelationshipName] = UnspecifiedName;
                change("Set the relationship of the embedded files that had none to Unspecified (AFRelationship)");
            }

            // The files are associated with the document (ISO 19005-3, 6.8)
            if (associated is null || !associated.Any(item => ReferenceEquals(PdfReference.ResolveIfReference(item), file.Dictionary)))
            {
                if (associated is null)
                {
                    associated = [];
                    document.Catalog[PdfEmbeddedFile.AFName] = associated;
                }

                associated.Add(file.Reference ?? document.Objects.Add(file.Dictionary));
                change("Associated the embedded files with the document (AF)");
            }
        }
    }

    /// <summary>Decodes the streams encoded with <c>LZWDecode</c>, which PDF/A-2 does not allow (ISO 19005-2, 6.1.7.2): the writer compresses them with <c>FlateDecode</c>.</summary>
    private static void ReencodeLzwStreams(PdfDocument document, Action<string> change, List<string> violations)
    {
        foreach (var reference in document.Objects.GetReferences().ToList())
        {
            PdfObject value;
            try
            {
                value = reference.Resolve()!;
            }
            catch (PdfException)
            {
                continue;
            }

            if (value is not PdfStream stream)
                continue;

            if (stream.Dictionary.ContainsKey(PdfNames.F))
            {
                violations.Add("A stream has its data in an external file (F): PDF/A does not allow it");
            }

            if (!PdfFilterPipeline.GetFilterNames(stream.Dictionary).Any(name => name.Value is "LZWDecode" or "LZW"))
                continue;

            try
            {
                var data = PdfFilterPipeline.Decode(stream, stopBeforeImageFilters: true, out var imageFilter);
                if (imageFilter is { } filter)
                {
                    stream.SetEncodedData(data, filter.Name, filter.Parameters);
                }
                else
                {
                    stream.SetData(data);
                }

                change("Decoded the LZWDecode streams: they are compressed with FlateDecode");
            }
            catch (PdfException)
            {
                violations.Add("A stream encoded with LZWDecode cannot be decoded");
            }
        }
    }

    /// <summary>
    /// Fixes the inline images of the content streams (ISO 19005-2, 6.1.7.2 and 6.2.9): the data of an image encoded with
    /// <c>LZWDecode</c> is decoded and written in hexadecimal, and the interpolation is turned off.
    /// </summary>
    private static void FixInlineImages(PdfDocument document, Action<string> change, List<string> violations)
    {
        var limits = document.Reader?.DecodeLimits ?? PdfDecodeLimits.Default;

        // The content streams of a page are one content: an operator and its operands may be in two streams (7.8.2).
        // A page whose content changes gets one stream, shared by the pages that have the same streams.
        var rewritten = new Dictionary<string, PdfReference?>(StringComparer.Ordinal);
        var pageNumber = 0;
        foreach (var page in document.Pages)
        {
            pageNumber++;
            var key = GetContentsKey(page);
            if (key is not null && rewritten.TryGetValue(key, out var shared))
            {
                if (shared is not null)
                {
                    page.Dictionary[PdfNames.Contents] = shared;
                }

                continue;
            }

            byte[] data;
            try
            {
                data = page.GetContentData();
            }
            catch (PdfException)
            {
                violations.Add($"The content of page {pageNumber} cannot be decoded: its inline images cannot be checked");
                continue;
            }

            PdfReference? reference = null;
            if (RewriteInlineImages(data, limits, change, violations) is { } content)
            {
                var stream = new PdfStream();
                stream.SetData(content);
                reference = document.Objects.Add(stream);
                page.Dictionary[PdfNames.Contents] = reference;
            }

            if (key is not null)
            {
                rewritten[key] = reference;
            }
        }

        foreach (var stream in GetContentStreams(document))
        {
            byte[] data;
            try
            {
                data = stream.GetDecodedData();
            }
            catch (PdfException)
            {
                continue;
            }

            if (RewriteInlineImages(data, limits, change, violations) is { } content)
            {
                stream.SetData(content);
            }
        }
    }

    /// <summary>Gets a key of the content streams of a page, the same for the pages that share them, or <see langword="null"/> when they are not indirect objects.</summary>
    private static string? GetContentsKey(PdfPage page)
    {
        var references = PdfReference.ResolveIfReference(page.Dictionary[PdfNames.Contents] ?? PdfNull.Instance) switch
        {
            PdfArray array => array.ToList(),
            _ => [page.Dictionary[PdfNames.Contents] ?? PdfNull.Instance],
        };
        if (!references.TrueForAll(item => item is PdfReference))
            return null;

        return string.Join(' ', references.Cast<PdfReference>().Select(reference => reference.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + reference.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Rewrites a content whose inline images need a fix; returns <see langword="null"/> when none does.</summary>
    private static byte[]? RewriteInlineImages(byte[] data, PdfDecodeLimits limits, Action<string> change, List<string> violations)
    {
        // Only the content that has inline images with these entries is read
        var span = data.AsSpan();
        if (span.IndexOf("ID"u8) < 0 || (span.IndexOf("LZW"u8) < 0 && span.IndexOf("/I"u8) < 0))
            return null;

        var reader = new ContentStreamReader(data);
        var operands = new List<PdfObject>();
        var operations = new List<(string Name, PdfObject[] Operands, byte[]? Data)>();
        var changed = false;
        while (reader.TryRead(operands, out var name))
        {
            if (name == "BI" && operands is [PdfDictionary inline, PdfString imageData])
            {
                var (fixedDictionary, fixedData) = FixInlineImage(inline, imageData.Bytes, limits, change, violations);
                changed |= fixedDictionary is not null;
                operations.Add((name, [fixedDictionary ?? inline], fixedData ?? imageData.Bytes.ToArray()));
                continue;
            }

            operations.Add((name, [.. operands], null));
        }

        if (!changed)
            return null;

        var writer = new Writing.PdfByteWriter(data.Length + 256);
        foreach (var (name, values, imageData) in operations)
        {
            ContentStreamWriter.Write(writer, name, values, imageData);
        }

        return writer.ToArray();
    }

    /// <summary>Fixes an inline image; returns <see langword="null"/> values when it is kept as it is.</summary>
    private static (PdfDictionary? Dictionary, byte[]? Data) FixInlineImage(PdfDictionary inline, ReadOnlyMemory<byte> data, PdfDecodeLimits limits, Action<string> change, List<string> violations)
    {
        var filterKey = inline.ContainsKey(PdfName.Create("Filter")) ? PdfName.Create("Filter") : PdfName.Create("F");
        var parametersKey = inline.ContainsKey(PdfName.Create("DecodeParms")) ? PdfName.Create("DecodeParms") : PdfName.Create("DP");
        var interpolateKey = inline.ContainsKey(PdfNames.Interpolate) ? PdfNames.Interpolate : PdfName.Create("I");
        var filters = inline[filterKey] switch
        {
            PdfName name => [name],
            PdfArray array => array.OfType<PdfName>().ToList(),
            _ => new List<PdfName>(),
        };
        var lzw = filters.Exists(name => name.Value is "LZW" or "LZWDecode");
        var interpolate = inline[interpolateKey] is PdfBoolean { Value: true };
        if (!lzw && !interpolate)
            return (null, null);

        var dictionary = inline.CloneShallow();
        if (interpolate)
        {
            dictionary[interpolateKey] = PdfBoolean.False;
            change("Turned off the interpolation of the images");
        }

        if (!lzw)
            return (dictionary, null);

        byte[] decoded;
        (PdfName Name, PdfDictionary? Parameters)? imageFilter;
        try
        {
            decoded = PdfFilterPipeline.Decode(data, PdfImageOccurrence.ExpandInlineDictionary(inline), limits, stopBeforeImageFilters: true, out imageFilter);
        }
        catch (PdfException)
        {
            violations.Add("An inline image encoded with LZWDecode cannot be decoded");
            return (null, null);
        }

        // The data is written in hexadecimal: binary data could hold the bytes that end an inline image
        dictionary.Remove(filterKey);
        dictionary.Remove(parametersKey);
        if (imageFilter is { } filter)
        {
            dictionary[filterKey] = new PdfArray(PdfName.Create("AHx"), filter.Name);
            dictionary[parametersKey] = new PdfArray(PdfNull.Instance, (PdfObject?)filter.Parameters ?? PdfNull.Instance);
        }
        else
        {
            dictionary[filterKey] = PdfName.Create("AHx");
        }

        change("Decoded the LZWDecode inline images");
        return (dictionary, Encoding.ASCII.GetBytes(Convert.ToHexString(decoded) + ">"));
    }

    /// <summary>Gets the content streams other than those of the pages: of the forms and tiling patterns of their resources, of the glyphs of the Type 3 fonts, and of the annotation appearances.</summary>
    private static List<PdfStream> GetContentStreams(PdfDocument document)
    {
        var result = new List<PdfStream>();
        var visited = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        void Add(PdfObject? value)
        {
            if (Resolve(value) is PdfStream stream && visited.Add(stream))
            {
                result.Add(stream);
            }
        }

        foreach (var page in document.Pages)
        {
            foreach (var annotation in page.Annotations)
            {
                foreach (var stream in GetAppearanceStreams(annotation))
                {
                    Add(stream);
                }
            }
        }

        ForEachResources(document, resources =>
        {
            foreach (var key in new[] { PdfNames.XObject, PdfNames.Pattern })
            {
                if (resources.GetResolved(key) is PdfDictionary items)
                {
                    foreach (var (_, value) in items)
                    {
                        if (Resolve(value) is PdfStream stream && (stream.Dictionary.GetName(PdfNames.Subtype) == PdfNames.Form || stream.Dictionary.ContainsKey(PatternTypeName)))
                        {
                            Add(stream);
                        }
                    }
                }
            }

            if (resources.GetResolved(PdfNames.Font) is PdfDictionary fonts)
            {
                foreach (var (_, value) in fonts)
                {
                    if (Resolve(value) is PdfDictionary font && font.GetResolved(PdfName.Create("CharProcs")) is PdfDictionary procedures)
                    {
                        foreach (var (_, procedure) in procedures)
                        {
                            Add(procedure);
                        }
                    }
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Fixes the resources (ISO 19005-2, 6.2): removes the PostScript XObjects, the reference XObjects, the alternate
    /// images and the OPI entries, turns the interpolation of the images off, and removes the transfer functions of the
    /// graphics states.
    /// </summary>
    private static void FixResources(PdfDocument document, Action<string> change)
    {
        ForEachResources(document, resources =>
        {
            if (resources.GetResolved(PdfNames.XObject) is PdfDictionary xobjects)
            {
                foreach (var (name, value) in xobjects.ToList())
                {
                    if (Resolve(value) is not PdfStream stream)
                        continue;

                    var dictionary = stream.Dictionary;
                    switch (dictionary.GetName(PdfNames.Subtype)?.Value)
                    {
                        case "PS":
                            xobjects.Remove(name);
                            change("Removed the PostScript XObjects");
                            break;
                        case "Form":
                            if (dictionary.Remove(RefName) | dictionary.Remove(PSName) | dictionary.Remove(OpiName))
                            {
                                change("Removed the Ref, PS and OPI entries of the forms");
                            }

                            break;
                        case "Image":
                            if (dictionary.Remove(AlternatesName) | dictionary.Remove(OpiName))
                            {
                                change("Removed the alternate images and the OPI entries of the images");
                            }

                            if (dictionary.Get<PdfBoolean>(PdfNames.Interpolate)?.Value is true)
                            {
                                dictionary[PdfNames.Interpolate] = PdfBoolean.False;
                                change("Turned off the interpolation of the images");
                            }

                            break;
                    }
                }
            }

            if (resources.GetResolved(PdfNames.ExtGState) is PdfDictionary states)
            {
                foreach (var (_, value) in states)
                {
                    if (Resolve(value) is not PdfDictionary state)
                        continue;

                    var removed = state.Remove(TRName);
                    if (state[TR2Name] is { } transfer && Resolve(transfer) is not PdfName { Value: "Default" })
                    {
                        state[TR2Name] = DefaultName;
                        removed = true;
                    }

                    if (removed)
                    {
                        change("Removed the transfer functions of the graphics states");
                    }
                }
            }
        });
    }

    /// <summary>Fixes the configurations of the optional content (ISO 19005-2, 6.9): each one has a name, and none has the AS entry.</summary>
    private static void FixOptionalContent(PdfDocument document, Action<string> change)
    {
        if (document.Catalog.Get<PdfDictionary>(OCPropertiesName) is not { } properties)
            return;

        var configurations = new List<PdfDictionary>();
        if (properties.Get<PdfDictionary>(PdfNames.D) is { } defaultConfiguration)
        {
            configurations.Add(defaultConfiguration);
        }

        if (properties.Get<PdfArray>(ConfigsName) is { } configs)
        {
            configurations.AddRange(configs.Select(PdfReference.ResolveIfReference).OfType<PdfDictionary>());
        }

        var index = 0;
        foreach (var configuration in configurations)
        {
            index++;
            if (configuration.Remove(ASName))
            {
                change("Removed the automatic states of the optional content (AS)");
            }

            if (configuration.GetText(PdfNames.Name) is null)
            {
                configuration[PdfNames.Name] = PdfString.FromText(index == 1 ? "Default" : "Configuration " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                change("Named the configurations of the optional content");
            }
        }
    }

    /// <summary>Embeds a substitute for each standard font that is not embedded (<see cref="SubstituteFont"/>); the save reports the other fonts that are not embedded.</summary>
    private static void EmbedSubstituteFonts(PdfDocument document, Action<string> change)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Embed(PdfObject? value)
        {
            if (Resolve(value) is not PdfDictionary font || !visited.Add(font) || IsEmbedded(font) || SubstituteFont.GetSubstitutableName(font) is not { } name)
                return;

            if (SubstituteFont.Embed(document, font, name))
            {
                change($"Embedded a substitute of the standard font {name} (Liberation, with its widths)");
            }
        }

        ForEachResources(document, resources =>
        {
            if (resources.GetResolved(PdfNames.Font) is PdfDictionary fonts)
            {
                foreach (var (_, value) in fonts)
                {
                    Embed(value);
                }
            }
        });

        if (document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)?.Get<PdfDictionary>(DefaultResourcesName)?.Get<PdfDictionary>(PdfNames.Font) is { } formFonts)
        {
            foreach (var (_, value) in formFonts)
            {
                Embed(value);
            }
        }
    }

    /// <summary>
    /// Gives the device color spaces a meaning (ISO 19005-2, 6.2.4.3): the output intent of the options when the
    /// document has none, and an sRGB <c>DefaultRGB</c> color space when the document paints with <c>DeviceRGB</c> and
    /// the output intent is not RGB. <c>DeviceCMYK</c> needs a CMYK output intent: the library has no CMYK profile.
    /// </summary>
    private static void FixColorSpaces(PdfDocument document, int part, PdfAConversionOptions options, Action<string> change, List<string> violations)
    {
        var scratch = new List<string>();
        var hasIntent = FindOutputProfile(document) is not null;
        var intentSpace = CheckOutputIntents(document, part, scratch);
        if (!hasIntent && options.OutputIntentProfile is { } profile)
        {
            var space = profile.Length >= 20 ? System.Text.Encoding.Latin1.GetString(profile.Span.Slice(16, 4)) : "";
            var components = space switch
            {
                "GRAY" => 1,
                "RGB " => 3,
                "CMYK" => 4,
                _ => 0,
            };
            if (components == 0)
            {
                violations.Add("The profile of the options is not an ICC profile in gray, RGB or CMYK");
            }
            else
            {
                var stream = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(components) });
                stream.SetData(profile.Span);
                var intent = new PdfDictionary
                {
                    [PdfNames.Type] = OutputIntentName,
                    [PdfNames.S] = PdfAOutputIntentName,
                    [OutputConditionIdentifierName] = PdfString.FromText("Custom"),
                    [DestOutputProfileName] = document.Objects.Add(stream),
                };
                document.Catalog[OutputIntentsName] = new PdfArray(document.Objects.Add(intent));
                intentSpace = space;
                change("Added the output intent with the profile of the options");
            }
        }

        // The device color spaces the content uses: the errors of the check with the other output intent
        var rgbErrors = new List<string>();
        CheckDeviceColorSpaces(document, part, "CMYK", rgbErrors);
        var cmykErrors = new List<string>();
        CheckDeviceColorSpaces(document, part, "RGB ", cmykErrors);
        if (rgbErrors.Count > 0 && intentSpace != "RGB ")
        {
            var srgb = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(3) });
            srgb.SetData(IccProfile.Srgb.Data.Span);
            var colorSpace = new PdfArray(ICCBasedName, document.Objects.Add(srgb));
            ForEachResources(document, resources =>
            {
                if (resources.GetResolved(PdfNames.ColorSpace) is not PdfDictionary colorSpaces)
                {
                    colorSpaces = [];
                    resources[PdfNames.ColorSpace] = colorSpaces;
                }

                colorSpaces[DefaultRgbName] ??= colorSpace;
            });
            change("Added an sRGB DefaultRGB color space: the output intent is not RGB");
        }

        if (cmykErrors.Count > 0 && intentSpace != "CMYK")
        {
            violations.Add("The document paints with DeviceCMYK: PDF/A needs a CMYK output intent (PdfAConversionOptions.OutputIntentProfile)");
        }
    }

    /// <summary>Removes from the document information the characters XML does not allow, so that the XMP metadata can repeat it.</summary>
    private static void FixInformation(PdfDocument document, Action<string> change)
    {
        var information = document.Information;
        string? Sanitize(string? value)
        {
            if (value is null)
                return null;

            var sanitized = XmpPacketWriter.Sanitize(value);
            if (!ReferenceEquals(sanitized, value))
            {
                change("Removed the characters XML does not allow from the document information");
            }

            return sanitized;
        }

        information.Title = Sanitize(information.Title);
        information.Author = Sanitize(information.Author);
        information.Subject = Sanitize(information.Subject);
        information.Keywords = Sanitize(information.Keywords);
        information.Creator = Sanitize(information.Creator);
        information.Producer = Sanitize(information.Producer);
    }
}
