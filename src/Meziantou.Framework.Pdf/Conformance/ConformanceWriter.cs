using System.Globalization;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Pdf.Metadata;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Conformance;

/// <summary>
/// Prepares a document for the conformance levels it claims when it is saved: PDF/A (ISO 19005-2, -3 and -4) and PDF/UA
/// (ISO 14289-1). The requirements the library can check are checked (encryption, embedded fonts, tagging, title and
/// language); the entries the levels need are written (output intent, XMP identification, viewer preferences, the
/// flags of the annotations).
/// </summary>
internal static class ConformanceWriter
{
    private const string PdfAIdNamespace = "http://www.aiim.org/pdfa/ns/id/";
    private const string PdfUAIdNamespace = "http://www.aiim.org/pdfua/ns/id/";

    private static readonly PdfName OutputIntentsName = PdfName.Create("OutputIntents");
    private static readonly PdfName ViewerPreferencesName = PdfName.Create("ViewerPreferences");
    private static readonly PdfName DisplayDocTitleName = PdfName.Create("DisplayDocTitle");
    private static readonly PdfName TabsName = PdfName.Create("Tabs");
    private static readonly PdfName OCPropertiesName = PdfName.Create("OCProperties");
    private static readonly PdfName AppearanceName = PdfName.Create("AP");
    private static readonly PdfName CaName = PdfName.Create("ca");
    private static readonly PdfName StrokeAlphaName = PdfName.Create("CA");
    private static readonly PdfName BMName = PdfName.Create("BM");
    private static readonly PdfName TRName = PdfName.Create("TR");
    private static readonly PdfName TR2Name = PdfName.Create("TR2");

    /// <exception cref="InvalidOperationException">The document does not meet a requirement of its conformance levels.</exception>
    public static void Apply(PdfDocument document)
    {
        var pdfA = document.PdfAConformance;
        var pdfUA = document.PdfUAConformance;
        if (pdfA == PdfAConformance.None && pdfUA == PdfUAConformance.None)
            return;

        var part = PdfALevel.GetPart(pdfA);
        var errors = new List<string>();
        if (pdfA != PdfAConformance.None && document.Encryption is not null)
        {
            errors.Add("PDF/A does not allow encryption");
        }

        foreach (var font in GetFontsNotEmbedded(document))
        {
            errors.Add($"The font '{font}' is not embedded: PDF/A and PDF/UA need the fonts embedded (use PdfFont.Load)");
        }

        if ((PdfALevel.IsTagged(pdfA) || pdfUA != PdfUAConformance.None) && !document.StructureTree.Exists)
        {
            errors.Add("The conformance level needs a tagged document: add the elements of its structure (PdfDocument.StructureTree)");
        }

        if (pdfUA != PdfUAConformance.None)
        {
            if (string.IsNullOrEmpty(document.Information.Title))
            {
                errors.Add("PDF/UA needs the title of the document (PdfDocument.Information.Title)");
            }

            if (string.IsNullOrEmpty(document.Language))
            {
                errors.Add("PDF/UA needs the language of the document (PdfDocument.Language)");
            }
        }

        if (pdfUA == PdfUAConformance.PdfUA2)
        {
            if (part is 1 or 2 or 3)
            {
                errors.Add($"PDF/UA-2 is based on PDF 2.0: it cannot be combined with PDF/A-{part}, which is based on PDF {(part == 1 ? "1.4" : "1.7")} (use PDF/A-4)");
            }

            CheckPdfUA2(document, errors);
        }

        CheckEmbeddedFiles(document, errors);
        if (part == 1)
        {
            CheckPdfA1(document, errors);
        }

        if (part is 1 or 2 or 3 && document.Version > PdfVersion.Pdf17)
        {
            errors.Add(part == 1
                ? $"PDF/A-1 is based on PDF 1.4: the version of the document is {document.Version}"
                : $"PDF/A-2 and PDF/A-3 are based on PDF 1.7: the version of the document is {document.Version}");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("The document does not conform to the levels it claims:\n" + string.Join('\n', errors.Distinct(StringComparer.Ordinal)));

        if (part == 4 || pdfUA == PdfUAConformance.PdfUA2)
        {
            document.Version = PdfVersion.Pdf20;
        }
        else if (part == 1)
        {
            // PDF/A-1 is PDF 1.4 (ISO 19005-1, 6.1.2): the writer writes a cross-reference table, without object streams
            document.Version = PdfVersion.Pdf14;
            document.Catalog.Remove(PdfNames.Version);
        }

        if (pdfA != PdfAConformance.None)
        {
            AddOutputIntent(document, part);
            SetAnnotationFlags(document);
        }

        if (pdfUA != PdfUAConformance.None)
        {
            if (document.Catalog.GetResolved(ViewerPreferencesName) is not PdfDictionary preferences)
            {
                preferences = [];
                document.Catalog[ViewerPreferencesName] = preferences;
            }

            preferences[DisplayDocTitleName] = PdfBoolean.True;

            // The annotations of every page are in the order of the structure (ISO 14289-1, 7.18.3; ISO 14289-2, 8.9.3.3)
            foreach (var page in document.Pages)
            {
                if (page.Annotations.Count > 0)
                {
                    page.Dictionary[TabsName] = PdfNames.S;
                }
            }
        }

        if (pdfUA == PdfUAConformance.PdfUA2)
        {
            document.StructureTree.UsePdf2Namespace();
        }

        // The identification of the levels: the XMP metadata, built from the document information so that both agree
        var properties = new List<XmpProperty>();
        if (pdfA != PdfAConformance.None)
        {
            properties.Add(new XmpProperty("pdfaid", PdfAIdNamespace, "part", part.ToString(CultureInfo.InvariantCulture)));
            if (part == 4)
            {
                properties.Add(new XmpProperty("pdfaid", PdfAIdNamespace, "rev", "2020"));
            }

            if (PdfALevel.GetConformance(pdfA) is { } conformance)
            {
                properties.Add(new XmpProperty("pdfaid", PdfAIdNamespace, "conformance", conformance));
            }
        }

        if (pdfUA == PdfUAConformance.PdfUA1)
        {
            properties.Add(new XmpProperty("pdfuaid", PdfUAIdNamespace, "part", "1"));
        }
        else if (pdfUA == PdfUAConformance.PdfUA2)
        {
            // ISO 14289-2, 5: the part and the revision
            properties.Add(new XmpProperty("pdfuaid", PdfUAIdNamespace, "part", "2"));
            properties.Add(new XmpProperty("pdfuaid", PdfUAIdNamespace, "rev", "2024"));
        }

        // PDF/A-1, -2 and -3 only know the schemas of XMP 2004 and 2005: the PDF/UA identification is described by an extension schema
        var describePdfUA = pdfUA != PdfUAConformance.None && part is 1 or 2 or 3;
        document.SetXmpMetadata(XmpPacketWriter.Write(document.Information, properties, describePdfUA));
    }

    /// <summary>
    /// Checks what PDF/A-1 (ISO 19005-1, based on PDF 1.4) forbids in the resources of the pages, of the forms and of
    /// the annotation appearances: transparency (6.4), the LZW filter (6.1.10) and the filters of later versions,
    /// optional content (6.1.13), interpolated and 16-bit images (6.2.4), transfer functions (6.2.8), ICC profiles of
    /// version 4 (6.2.3), and the annotations it does not allow (6.5).
    /// </summary>
    private static void CheckPdfA1(PdfDocument document, List<string> errors)
    {
        if (document.Catalog.ContainsKey(OCPropertiesName))
        {
            errors.Add("PDF/A-1 does not allow optional content (OCProperties)");
        }

        var visitedColorSpaces = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        void CheckStream(PdfStream stream, string what)
        {
            foreach (var filter in stream.Filters)
            {
                if (filter == PdfNames.LZWDecode || filter == PdfNames.LZW)
                {
                    errors.Add($"PDF/A-1 does not allow the LZWDecode filter ({what})");
                }
                else if (filter == PdfNames.JPXDecode || filter == PdfNames.JBIG2Decode)
                {
                    errors.Add($"PDF/A-1 is based on PDF 1.4, which does not have the {filter.Value} filter ({what})");
                }
            }
        }

        void CheckColorSpace(PdfObject? value, int depth)
        {
            if (value is null || depth > 8 || PdfReference.ResolveIfReference(value) is not PdfArray { Count: > 1 } array || !visitedColorSpaces.Add(array))
                return;

            switch (array.GetResolved(0) is PdfName family ? family.Value : null)
            {
                case "ICCBased" when array.GetResolved(1) is PdfStream profile:
                    var data = profile.GetDecodedData();
                    if (data.Length >= 12 && data[8] >= 3)
                    {
                        errors.Add($"PDF/A-1 needs ICC profiles of version 2 (PDF 1.4): a profile is of version {data[8]}");
                    }

                    break;
                case "Indexed" or "Pattern":
                    CheckColorSpace(array[1], depth + 1);
                    break;
                case "Separation" or "DeviceN" when array.Count > 2:
                    CheckColorSpace(array[2], depth + 1);
                    break;
            }
        }

        void CheckGroup(PdfDictionary dictionary, string what)
        {
            if (dictionary.Get<PdfDictionary>(PdfNames.Group) is { } group && group.GetName(PdfNames.S)?.Value == "Transparency")
            {
                errors.Add($"PDF/A-1 does not allow transparency: {what} is a transparency group");
            }
        }

        foreach (var page in document.Pages)
        {
            CheckGroup(page.Dictionary, "a page");
            foreach (var content in page.Dictionary.GetResolved(PdfNames.Contents) switch
            {
                PdfStream stream => [stream],
                PdfArray array => array.Select(PdfReference.ResolveIfReference).OfType<PdfStream>().ToArray(),
                _ => [],
            })
            {
                CheckStream(content, "a content stream");
            }

            foreach (var annotation in page.Annotations)
            {
                var dictionary = annotation.Dictionary;
                if (annotation.Subtype is "FileAttachment" or "Sound" or "Movie")
                {
                    errors.Add($"PDF/A-1 does not allow the {annotation.Subtype} annotations (ISO 19005-1, 6.5.2)");
                }

                if (dictionary.Get<PdfNumber>(StrokeAlphaName) is { } alpha && alpha.DoubleValue != 1)
                {
                    errors.Add("PDF/A-1 does not allow transparency: an annotation has a constant alpha other than 1 (CA)");
                }

                if (dictionary.Get<PdfDictionary>(AppearanceName) is { } appearances && appearances.Any(entry => entry.Key != PdfNames.N))
                {
                    errors.Add("PDF/A-1 allows only the normal appearance of the annotations (the N entry of AP, ISO 19005-1, 6.5.3)");
                }
            }
        }

        ForEachResources(document, resources =>
        {
            if (resources.Get<PdfDictionary>(PdfNames.ExtGState) is { } states)
            {
                foreach (var (_, value) in states)
                {
                    if (PdfReference.ResolveIfReference(value) is not PdfDictionary state)
                        continue;

                    if (state.GetResolved(PdfNames.SMask) is { } mask && mask is not PdfName { Value: "None" })
                    {
                        errors.Add("PDF/A-1 does not allow transparency: a graphics state has a soft mask (SMask)");
                    }

                    foreach (var key in new[] { StrokeAlphaName, CaName })
                    {
                        if (state.Get<PdfNumber>(key) is { } alpha && alpha.DoubleValue != 1)
                        {
                            errors.Add($"PDF/A-1 does not allow transparency: a graphics state has a constant alpha other than 1 ({key.Value})");
                        }
                    }

                    var blendMode = state.GetResolved(BMName) switch
                    {
                        PdfName name => name.Value,
                        PdfArray { Count: > 0 } modes => (modes.GetResolved(0) as PdfName)?.Value,
                        _ => null,
                    };
                    if (blendMode is not null and not ("Normal" or "Compatible"))
                    {
                        errors.Add($"PDF/A-1 does not allow transparency: a graphics state has the blend mode {blendMode}");
                    }

                    if (state.ContainsKey(TRName) || (state.GetResolved(TR2Name) is { } transfer && transfer is not PdfName { Value: "Default" }))
                    {
                        errors.Add("PDF/A-1 does not allow transfer functions (TR and TR2 of a graphics state)");
                    }
                }
            }

            if (resources.Get<PdfDictionary>(PdfNames.ColorSpace) is { } colorSpaces)
            {
                foreach (var (_, value) in colorSpaces)
                {
                    CheckColorSpace(value, depth: 0);
                }
            }

            if (resources.Get<PdfDictionary>(PdfNames.XObject) is { } xobjects)
            {
                foreach (var (_, value) in xobjects)
                {
                    if (PdfReference.ResolveIfReference(value) is not PdfStream xobject)
                        continue;

                    var dictionary = xobject.Dictionary;
                    if (dictionary.GetName(PdfNames.Subtype) == PdfNames.Form)
                    {
                        CheckGroup(dictionary, "a form");
                        continue;
                    }

                    CheckStream(xobject, "an image");
                    if (dictionary.ContainsKey(PdfNames.SMask))
                    {
                        errors.Add("PDF/A-1 does not allow transparency: an image has a soft mask (SMask), such as an alpha channel");
                    }

                    if (dictionary.Get<PdfBoolean>(PdfNames.Interpolate)?.Value is true)
                    {
                        errors.Add("PDF/A-1 does not allow the interpolation of images (Interpolate)");
                    }

                    if (dictionary.GetInteger(PdfNames.BitsPerComponent, 8) > 8)
                    {
                        errors.Add("PDF/A-1 does not allow images of more than 8 bits per component");
                    }

                    CheckColorSpace(dictionary[PdfNames.ColorSpace], depth: 0);
                }
            }
        });

        foreach (var annotation in document.Pages.SelectMany(page => page.Annotations))
        {
            foreach (var appearance in GetAppearanceStreams(annotation))
            {
                CheckGroup(appearance.Dictionary, "an annotation appearance");
            }
        }
    }

    /// <summary>
    /// Checks the structure and the navigation of PDF/UA-2 (ISO 14289-2): one <c>Document</c> element at the top of the
    /// structure tree (8.2.5.2), no <c>H</c> (8.2.5.12) or <c>Note</c> (8.2.5.14) element, and structure destinations
    /// for the destinations in the document (8.8).
    /// </summary>
    private static void CheckPdfUA2(PdfDocument document, List<string> errors)
    {
        var tree = document.StructureTree;
        if (!tree.Exists)
            return;

        var roleMap = tree.RoleMap;
        string GetStandardType(string type)
        {
            for (var i = 0; i < 16 && roleMap.TryGetValue(type, out var mapped); i++)
            {
                type = mapped;
            }

            return type;
        }

        var top = tree.Elements;
        if (top.Count != 1 || GetStandardType(top[0].Type) != "Document")
        {
            errors.Add("PDF/UA-2 needs a single Document element at the top of the structure tree (ISO 14289-2, 8.2.5.2)");
        }

        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Visit(IReadOnlyList<PdfStructureElement> elements, int depth)
        {
            if (depth > 64)
                return;

            foreach (var element in elements)
            {
                if (!visited.Add(element.Dictionary))
                    continue;

                switch (GetStandardType(element.Type))
                {
                    case "H":
                        errors.Add("PDF/UA-2 does not allow the H structure type: use H1 to H6 (ISO 14289-2, 8.2.5.12)");
                        break;
                    case "Note":
                        errors.Add("PDF/UA-2 does not allow the Note structure type: use FENote (ISO 14289-2, 8.2.5.14)");
                        break;
                }

                Visit(element.Children, depth + 1);
            }
        }

        Visit(top, depth: 0);

        void VisitOutline(IEnumerable<PdfOutlineItem> items, int depth)
        {
            if (depth > 64)
                return;

            foreach (var item in items)
            {
                if (item.Destination is { StructureElement: null })
                {
                    errors.Add($"PDF/UA-2 needs structure destinations: the outline item '{item.Title}' goes to a page (use PdfDestination.WithStructureElement, ISO 14289-2, 8.8)");
                }

                VisitOutline(item.Children, depth + 1);
            }
        }

        VisitOutline(document.Outlines, depth: 0);
        foreach (var page in document.Pages)
        {
            foreach (var annotation in page.Annotations)
            {
                if (annotation is PdfLinkAnnotation { Destination.StructureElement: null })
                {
                    errors.Add("PDF/UA-2 needs structure destinations: a link goes to a page (use PdfDestination.WithStructureElement, ISO 14289-2, 8.8)");
                }
            }
        }

        // ISO 14289-2, 8.14.1 and 8.9.2.4.10: the files of the document have a description, the attached files a relationship
        foreach (var file in document.EmbeddedFiles)
        {
            if (file.Description is null)
            {
                errors.Add($"PDF/UA-2 needs the description of the embedded file '{file.FileName}' (PdfEmbeddedFileOptions.Description)");
            }
        }

        foreach (var annotation in document.Pages.SelectMany(page => page.Annotations).OfType<PdfFileAttachmentAnnotation>())
        {
            if (annotation.File is { } file && file.Dictionary.GetName(PdfEmbeddedFile.AFRelationshipName) is null)
            {
                errors.Add($"PDF/UA-2 needs the AFRelationship entry of the file '{file.FileName}' of a file attachment annotation");
            }
        }
    }

    /// <summary>
    /// Checks the embedded files (ISO 19005-1, 6.1.11; ISO 19005-2, 6.8; ISO 19005-3, 6.8; ISO 19005-4, 6.9): PDF/A-1
    /// has none, PDF/A-2, PDF/A-4 and PDF/A-4e embed PDF/A documents only, PDF/A-3 and PDF/A-4f any file whose file
    /// specification has a name, a MIME type and a relationship, and PDF/A-4f has at least one.
    /// </summary>
    private static void CheckEmbeddedFiles(PdfDocument document, List<string> errors)
    {
        var pdfA = document.PdfAConformance;
        var part = PdfALevel.GetPart(pdfA);
        if (part == 0)
            return;

        var files = GetEmbeddedFiles(document);
        if (part == 1)
        {
            if (files.Count > 0 || document.EmbeddedFiles.Count > 0)
            {
                errors.Add("PDF/A-1 does not allow embedded files (ISO 19005-1, 6.1.11)");
            }

            return;
        }

        if (pdfA == PdfAConformance.PdfA4F && document.EmbeddedFiles.Count == 0)
        {
            errors.Add("PDF/A-4f needs at least one embedded file in PdfDocument.EmbeddedFiles (ISO 19005-4, 6.9)");
        }

        foreach (var file in files)
        {
            var name = file.FileName;
            if (file.Dictionary.GetText(PdfNames.F) is null || file.Dictionary.GetText(PdfEmbeddedFile.UFName) is null)
            {
                errors.Add($"The file specification of the embedded file '{name}' needs the F and UF entries");
            }

            if (pdfA is not (PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A or PdfAConformance.PdfA4F))
            {
                // ISO 19005-2, 6.8 and ISO 19005-4, 6.9: the embedded files are PDF/A documents
                int[] parts = part == 4 ? [1, 2, 4] : [1, 2];
                var embeddedPart = GetPdfAPart(file);
                if (embeddedPart is null || !parts.Contains(embeddedPart.Value))
                {
                    errors.Add($"The embedded file '{name}' is not a PDF/A-{string.Join(", PDF/A-", parts)} document: {pdfA} embeds PDF/A documents only");
                }
            }

            if (part is 3 or 4)
            {
                if (file.MimeType is null)
                {
                    errors.Add($"The embedded file '{name}' needs a MIME type (the Subtype of its stream)");
                }

                if (file.Dictionary.GetName(PdfEmbeddedFile.AFRelationshipName) is null)
                {
                    errors.Add($"The file specification of the embedded file '{name}' needs the AFRelationship entry");
                }
            }
        }
    }

    /// <summary>Gets the files of the name tree of the document and of the file attachment annotations of its pages.</summary>
    private static List<PdfEmbeddedFile> GetEmbeddedFiles(PdfDocument document)
    {
        var result = document.EmbeddedFiles.Where(file => file.Stream is not null).ToList();
        var visited = new HashSet<PdfDictionary>(result.Select(file => file.Dictionary), ReferenceEqualityComparer.Instance);
        foreach (var page in document.Pages)
        {
            foreach (var annotation in page.Annotations)
            {
                if (annotation is PdfFileAttachmentAnnotation { File: { } file } && file.Stream is not null && visited.Add(file.Dictionary))
                {
                    result.Add(file);
                }
            }
        }

        return result;
    }

    /// <summary>Gets the part of ISO 19005 an embedded file claims, or <see langword="null"/> when it is not a PDF/A document.</summary>
    private static int? GetPdfAPart(PdfEmbeddedFile file)
    {
        try
        {
            var embedded = PdfDocument.Open(file.GetData());
            return embedded.GetXmpMetadata() is { } metadata ? XmpIdentification.ReadPdfAPart(metadata) : null;
        }
        catch (PdfException)
        {
            return null;
        }
    }

    /// <summary>
    /// The output intent of PDF/A (ISO 19005-1, 6.2.2; ISO 19005-2, 6.2.3): the sRGB profile, which gives the meaning of
    /// the device color spaces; a profile of version 2 for PDF/A-1, whose PDF 1.4 does not know the later versions.
    /// </summary>
    private static void AddOutputIntent(PdfDocument document, int part)
    {
        if (document.Catalog.Get<PdfArray>(OutputIntentsName) is { Count: > 0 } intents && intents.Any(intent => PdfReference.ResolveIfReference(intent) is PdfDictionary dictionary && dictionary.GetName(PdfNames.S)?.Value == "GTS_PDFA1"))
            return;

        var profile = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(3) });
        profile.SetData(part == 1 ? SrgbV2Profile.Data : IccProfile.Srgb.Data.Span);
        var intent = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("OutputIntent"),
            [PdfNames.S] = PdfName.Create("GTS_PDFA1"),
            [PdfName.Create("OutputConditionIdentifier")] = PdfString.FromText("sRGB IEC61966-2.1"),
            [PdfName.Create("Info")] = PdfString.FromText("sRGB IEC61966-2.1"),
            [PdfName.Create("DestOutputProfile")] = document.Objects.Add(profile),
        };
        document.Catalog[OutputIntentsName] = new PdfArray(document.Objects.Add(intent));
    }

    /// <summary>The annotations of PDF/A are printed and visible (ISO 19005-2, 6.3.2): the Print flag is set, the Hidden, Invisible, NoView and ToggleNoView flags are cleared.</summary>
    private static void SetAnnotationFlags(PdfDocument document)
    {
        const int Invisible = 1;
        const int Hidden = 2;
        const int Print = 4;
        const int NoView = 32;
        const int ToggleNoView = 256;
        foreach (var page in document.Pages)
        {
            foreach (var annotation in page.Annotations)
            {
                var flags = (int)annotation.Dictionary.GetInteger(PdfNames.F, 0);
                var expected = (flags | Print) & ~(Invisible | Hidden | NoView | ToggleNoView);
                if (flags != expected)
                {
                    annotation.Dictionary[PdfNames.F] = PdfInteger.Get(expected);
                }
            }
        }
    }

    /// <summary>Gets the names of the fonts the pages, forms, patterns and annotation appearances use without embedding them.</summary>
    private static List<string> GetFontsNotEmbedded(PdfDocument document)
    {
        var result = new List<string>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        ForEachResources(document, resources =>
        {
            if (resources.GetResolved(PdfNames.Font) is not PdfDictionary fonts)
                return;

            foreach (var (_, value) in fonts)
            {
                if (PdfReference.ResolveIfReference(value) is PdfDictionary font && visited.Add(font) && !IsEmbedded(font))
                {
                    var name = font.GetName(PdfNames.BaseFont)?.Value ?? "(unnamed)";
                    if (!result.Contains(name, StringComparer.Ordinal))
                    {
                        result.Add(name);
                    }
                }
            }
        });

        return result;
    }

    /// <summary>Runs an action on each resource dictionary of the pages, forms, patterns, Type 3 fonts and annotation appearances, once.</summary>
    private static void ForEachResources(PdfDocument document, Action<PdfDictionary> action)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void VisitResources(PdfDictionary? resources, int depth)
        {
            if (resources is null || depth > 32 || !visited.Add(resources))
                return;

            action(resources);
            if (resources.GetResolved(PdfNames.Font) is PdfDictionary fonts)
            {
                foreach (var (_, value) in fonts)
                {
                    // The glyphs of a Type 3 font have resources of their own
                    if (PdfReference.ResolveIfReference(value) is PdfDictionary type3 && type3.GetName(PdfNames.Subtype) == PdfNames.Type3)
                    {
                        VisitResources(type3.GetResolved(PdfNames.Resources) as PdfDictionary, depth + 1);
                    }
                }
            }

            foreach (var key in new[] { PdfNames.XObject, PdfNames.Pattern })
            {
                if (resources.GetResolved(key) is PdfDictionary items)
                {
                    foreach (var (_, value) in items)
                    {
                        var dictionary = PdfReference.ResolveIfReference(value) switch
                        {
                            PdfStream stream => stream.Dictionary,
                            PdfDictionary d => d,
                            _ => null,
                        };
                        VisitResources(dictionary?.GetResolved(PdfNames.Resources) as PdfDictionary, depth + 1);
                    }
                }
            }
        }

        foreach (var page in document.Pages)
        {
            VisitResources(page.Dictionary.GetResolved(PdfNames.Resources) as PdfDictionary, depth: 0);
            foreach (var annotation in page.Annotations)
            {
                foreach (var state in GetAppearanceStreams(annotation))
                {
                    VisitResources(state.Dictionary.GetResolved(PdfNames.Resources) as PdfDictionary, depth: 1);
                }
            }
        }
    }

    /// <summary>Gets the appearance streams of an annotation (12.5.5): the normal, rollover and down appearances, and their states.</summary>
    private static List<PdfStream> GetAppearanceStreams(PdfAnnotation annotation)
    {
        if (annotation.Dictionary.GetResolved(AppearanceName) is not PdfDictionary appearances)
            return [];

        var result = new List<PdfStream>();
        foreach (var (_, value) in appearances)
        {
            switch (PdfReference.ResolveIfReference(value))
            {
                case PdfStream stream:
                    result.Add(stream);
                    break;
                case PdfDictionary states:
                    result.AddRange(states.Select(entry => PdfReference.ResolveIfReference(entry.Value)).OfType<PdfStream>());
                    break;
            }
        }

        return result;
    }

    private static bool IsEmbedded(PdfDictionary font)
    {
        if (font.GetName(PdfNames.Subtype) == PdfNames.Type3)
            return true;

        if (font.GetName(PdfNames.Subtype) == PdfNames.Type0)
        {
            if (font.GetResolved(PdfNames.DescendantFonts) is not PdfArray { Count: > 0 } descendants || descendants.GetResolved(0) is not PdfDictionary descendant)
                return false;

            font = descendant;
        }

        return font.GetResolved(PdfNames.FontDescriptor) is PdfDictionary descriptor
            && (descriptor.ContainsKey(PdfNames.FontFile) || descriptor.ContainsKey(PdfNames.FontFile2) || descriptor.ContainsKey(PdfNames.FontFile3));
    }
}
