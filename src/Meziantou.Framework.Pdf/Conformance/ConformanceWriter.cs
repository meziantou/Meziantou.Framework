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

    /// <exception cref="InvalidOperationException">The document does not meet a requirement of its conformance levels.</exception>
    public static void Apply(PdfDocument document)
    {
        var pdfA = document.PdfAConformance;
        var pdfUA = document.PdfUAConformance;
        if (pdfA == PdfAConformance.None && pdfUA == PdfUAConformance.None)
            return;

        var errors = new List<string>();
        if (pdfA != PdfAConformance.None && document.Encryption is not null)
        {
            errors.Add("PDF/A does not allow encryption");
        }

        foreach (var font in GetFontsNotEmbedded(document))
        {
            errors.Add($"The font '{font}' is not embedded: PDF/A and PDF/UA need the fonts embedded (use PdfFont.Load)");
        }

        if ((pdfA is PdfAConformance.PdfA2A or PdfAConformance.PdfA3A || pdfUA != PdfUAConformance.None) && !document.StructureTree.Exists)
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

        CheckEmbeddedFiles(document, errors);

        var isPdfA4 = pdfA == PdfAConformance.PdfA4;
        if (pdfA != PdfAConformance.None && !isPdfA4 && document.Version > PdfVersion.Pdf17)
        {
            errors.Add($"PDF/A-2 and PDF/A-3 are based on PDF 1.7: the version of the document is {document.Version}");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("The document does not conform to the levels it claims:\n" + string.Join('\n', errors));

        if (isPdfA4)
        {
            document.Version = PdfVersion.Pdf20;
        }

        if (pdfA != PdfAConformance.None)
        {
            AddOutputIntent(document);
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
        }

        // The identification of the levels: the XMP metadata, built from the document information so that both agree
        var properties = new List<XmpProperty>();
        if (pdfA != PdfAConformance.None)
        {
            var (part, conformance) = pdfA switch
            {
                PdfAConformance.PdfA2B => ("2", "B"),
                PdfAConformance.PdfA2U => ("2", "U"),
                PdfAConformance.PdfA2A => ("2", "A"),
                PdfAConformance.PdfA3B => ("3", "B"),
                PdfAConformance.PdfA3U => ("3", "U"),
                PdfAConformance.PdfA3A => ("3", "A"),
                _ => ("4", null),
            };
            properties.Add(new XmpProperty("pdfaid", PdfAIdNamespace, "part", part));
            if (isPdfA4)
            {
                properties.Add(new XmpProperty("pdfaid", PdfAIdNamespace, "rev", "2020"));
            }

            if (conformance is not null)
            {
                properties.Add(new XmpProperty("pdfaid", PdfAIdNamespace, "conformance", conformance));
            }
        }

        if (pdfUA != PdfUAConformance.None)
        {
            properties.Add(new XmpProperty("pdfuaid", PdfUAIdNamespace, "part", "1"));
        }

        // PDF/A-2 and PDF/A-3 only know the schemas of XMP 2005: the PDF/UA identification is described by an extension schema
        var describePdfUA = pdfUA != PdfUAConformance.None && pdfA != PdfAConformance.None && !isPdfA4;
        document.SetXmpMetadata(XmpPacketWriter.Write(document.Information, properties, describePdfUA));
    }

    /// <summary>
    /// Checks the embedded files (ISO 19005-2, 6.8; ISO 19005-3, 6.8; ISO 19005-4, 6.9): PDF/A-2 and PDF/A-4 embed PDF/A
    /// documents only, PDF/A-3 any file whose file specification has a name, a MIME type and a relationship.
    /// </summary>
    private static void CheckEmbeddedFiles(PdfDocument document, List<string> errors)
    {
        var pdfA = document.PdfAConformance;
        if (pdfA == PdfAConformance.None)
            return;

        foreach (var file in GetEmbeddedFiles(document))
        {
            var name = file.FileName;
            if (file.Dictionary.GetText(PdfNames.F) is null || file.Dictionary.GetText(PdfEmbeddedFile.UFName) is null)
            {
                errors.Add($"The file specification of the embedded file '{name}' needs the F and UF entries");
            }

            if (pdfA is PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA2A or PdfAConformance.PdfA4)
            {
                // ISO 19005-2, 6.8 and ISO 19005-4, 6.9: the embedded files are PDF/A documents
                int[] parts = pdfA == PdfAConformance.PdfA4 ? [1, 2, 4] : [1, 2];
                var part = GetPdfAPart(file);
                if (part is null || !parts.Contains(part.Value))
                {
                    errors.Add($"The embedded file '{name}' is not a PDF/A-{string.Join(", PDF/A-", parts)} document: {pdfA} embeds PDF/A documents only");
                }
            }

            if (pdfA is PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A or PdfAConformance.PdfA4)
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

    /// <summary>The output intent of PDF/A (ISO 19005-2, 6.2.3): the sRGB profile, which gives the meaning of the device color spaces.</summary>
    private static void AddOutputIntent(PdfDocument document)
    {
        if (document.Catalog.Get<PdfArray>(OutputIntentsName) is { Count: > 0 } intents && intents.Any(intent => PdfReference.ResolveIfReference(intent) is PdfDictionary dictionary && dictionary.GetName(PdfNames.S)?.Value == "GTS_PDFA1"))
            return;

        var profile = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(3) });
        profile.SetData(IccProfile.Srgb.Data.Span);
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
        void VisitResources(PdfDictionary? resources, int depth)
        {
            if (resources is null || depth > 32 || !visited.Add(resources))
                return;

            if (resources.GetResolved(PdfNames.Font) is PdfDictionary fonts)
            {
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
                if (annotation.Dictionary.GetResolved(PdfName.Create("AP")) is PdfDictionary appearances)
                {
                    foreach (var (_, value) in appearances)
                    {
                        var states = PdfReference.ResolveIfReference(value) switch
                        {
                            PdfStream stream => [stream],
                            PdfDictionary d => d.Select(entry => PdfReference.ResolveIfReference(entry.Value)).OfType<PdfStream>().ToArray(),
                            _ => [],
                        };
                        foreach (var state in states)
                        {
                            VisitResources(state.Dictionary.GetResolved(PdfNames.Resources) as PdfDictionary, depth: 1);
                        }
                    }
                }
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
