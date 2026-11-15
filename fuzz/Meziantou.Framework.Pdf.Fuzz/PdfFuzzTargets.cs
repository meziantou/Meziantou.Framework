using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Pdf.Fuzz;

/// <summary>
/// The form data readers (FDF and XFDF) only throw <see cref="PdfException"/>, and what they read is written back to a
/// fixed point: written, read and written again, the data gives the same file. The tools that rewrite documents keep
/// them: the writer is deterministic (<see cref="PdfRoundTripValidator"/>), and the optimizer and the sanitizer give files
/// that can be read back with the same pages.
/// </summary>
public static class PdfFuzzTargets
{
    private static readonly PdfReadOptions ReadOptions = new()
    {
        MaxDecodedStreamLength = 4 * 1024 * 1024,
        MaxNestingDepth = 64,
    };

    private static readonly string[] FdfTokens =
    [
        "%FDF-1.2", "%PDF-1.7", "%%EOF", "obj", "endobj", "trailer", "xref", "startxref", "<<", ">>", "[", "]", "(", ")", "<", ">", "R", "1 0 R", "1 0 obj",
        "/Root", "/FDF", "/Fields", "/Kids", "/T", "/V", "/F", "/UF", "/Type", "/Filespec", "/Off", "/Yes", "<FEFF", "null", "true", "\\(", "\\)", "\\\\", "#20",
    ];

    private static readonly string[] XfdfTokens =
    [
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "<xfdf xmlns=\"http://ns.adobe.com/xfdf/\" xml:space=\"preserve\">", "</xfdf>", "<fields>", "</fields>",
        "<field name=\"", "\">", "</field>", "<value>", "</value>", "<value/>", "<f href=\"", "\"/>", "<annots/>", "<value-richtext>", "&amp;", "&lt;", "&#x1;", "<!DOCTYPE",
        "<![CDATA[", "]]>", ".",
    ];

    private static readonly string[] PdfTokens =
    [
        "%PDF-1.7", "%%EOF", "obj", "endobj", "stream", "endstream", "xref", "trailer", "startxref", "<<", ">>", "[", "]", "R", "null",
        "/Type", "/Catalog", "/Pages", "/Page", "/Kids", "/Count", "/Parent", "/MediaBox", "/CropBox", "/Resources", "/Contents", "/Font", "/XObject",
        "/Outlines", "/First", "/Info", "/Metadata", "/Names", "/JavaScript", "/OpenAction", "/AA", "/AcroForm", "/XFA", "/Annots", "/Thumb",
        "/OCProperties", "/OC", "/OCGs", "/D", "/OFF", "/BDC", "/EMC", "/Length", "/Filter", "/FlateDecode",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("pdf-fdf", Fdf) { Seeds = () => GetSeeds(".fdf"), Dictionary = FdfTokens },
        new("pdf-xfdf", Xfdf) { Seeds = () => GetSeeds(".xfdf"), Dictionary = XfdfTokens },
        new("pdf-round-trip", RoundTrip) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-optimizer", Optimizer) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
    ];

    /// <summary>
    /// The input is an FDF file. Reading it only throws <see cref="PdfException"/>; the data read is written to a fixed
    /// point (FDF and XFDF), and the XFDF it writes reads back with the same names.
    /// </summary>
    private static void Fdf(ReadOnlySpan<byte> data)
    {
        PdfFormData formData;
        try
        {
            formData = PdfFormData.ParseFdf(data, ReadOptions);
        }
        catch (PdfException)
        {
            return;
        }

        CheckFixedPoint(formData);
    }

    /// <summary>
    /// The input is an XFDF document. Reading it only throws <see cref="InvalidPdfContentException"/>; the data read is
    /// written to a fixed point, and the XFDF written reads back as the same data.
    /// </summary>
    private static void Xfdf(ReadOnlySpan<byte> data)
    {
        PdfFormData formData;
        try
        {
            formData = PdfFormData.ParseXfdf(FuzzText.Decode(data));
        }
        catch (InvalidPdfContentException)
        {
            return;
        }

        CheckFixedPoint(formData);
    }

    private static void CheckFixedPoint(PdfFormData formData)
    {
        foreach (var field in formData.Fields)
        {
            FuzzAssert.True(field.Values.Count > 0 || field.Value is null, "A field without values has a value");
        }

        var fdf = formData.ToFdf();
        var fromFdf = PdfFormData.ParseFdf(fdf);
        FuzzAssert.True(PdfFormData.ParseFdf(fromFdf.ToFdf()).ToFdf().AsSpan().SequenceEqual(fromFdf.ToFdf()), "The FDF written is not a fixed point");
        FuzzAssert.Equal(formData.SourceFile is null, fromFdf.SourceFile is null, "The FDF written lost the source file");

        var xfdf = formData.ToXfdf();
        var fromXfdf = PdfFormData.ParseXfdf(xfdf);
        FuzzAssert.Equal(xfdf, fromXfdf.ToXfdf(), "The XFDF written is not a fixed point");
        var names = new HashSet<string>(fromXfdf.Fields.Select(field => field.Name), StringComparer.Ordinal);
        foreach (var field in fromFdf.Fields)
        {
            if (field.Values.Count > 0)
            {
                FuzzAssert.True(names.Contains(field.Name), $"The field '{FuzzText.Escape(field.Name)}' is missing from the XFDF written");
            }
        }
    }

    /// <summary>The input is a PDF file: when it can be read and written, the writer is deterministic and keeps its pages.</summary>
    private static void RoundTrip(ReadOnlySpan<byte> data)
    {
        PdfRoundTripResult result;
        try
        {
            result = PdfRoundTripValidator.Validate(data.ToArray(), ReadOptions);
        }
        catch (PdfException)
        {
            return;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No object number", StringComparison.Ordinal))
        {
            return;
        }

        FuzzAssert.True(result.Succeeded, $"The round trip failed: {result.Failure}");
    }

    /// <summary>
    /// The input is a PDF file. The optimizer plans every change, the plan is valid JSON, and the optimized document is
    /// written and read back with the same pages. The sanitizer keeps the bytes of the file and has nothing left to repair
    /// in its output.
    /// </summary>
    private static void Optimizer(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        PdfDocument document;
        int pageCount;
        try
        {
            document = PdfDocument.Open(bytes, ReadOptions);
            pageCount = document.Pages.Count;
        }
        catch (PdfException)
        {
            return;
        }

        try
        {
            var plan = PdfOptimizer.CreatePlan(document, new PdfOptimizationOptions
            {
                RemoveMetadata = true,
                RemoveJavaScript = true,
                RemoveAttachments = true,
                RemovePageThumbnails = true,
                RemoveComments = true,
                RemoveOpenAction = true,
                RemoveXfa = true,
                RemoveFormFields = true,
                RemoveBookmarks = true,
                FlattenOptionalContent = true,
                PruneUnusedResources = true,
                PruneUnreachableObjects = true,
                CompressStreams = true,
                MaxImageResolution = 36,
                AllowSignatureInvalidation = true,
            });
            using (System.Text.Json.JsonDocument.Parse(plan.ToJson()))
            {
            }

            PdfOptimizer.Apply(plan);
            var written = document.ToByteArray();
            var read = PdfDocument.Open(written, ReadOptions);
            FuzzAssert.False(read.IsRepaired, "The optimized document is repaired when it is read");
            FuzzAssert.Equal(pageCount, read.Pages.Count, "The optimizer changed the number of pages");
        }
        catch (PdfException)
        {
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No object number", StringComparison.Ordinal))
        {
        }

        PdfSanitizationResult sanitized;
        try
        {
            sanitized = PdfSaveSanitizer.Sanitize(bytes, ReadOptions);
        }
        catch (PdfException)
        {
            return;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No object number", StringComparison.Ordinal) || ex.Message.Contains("rebuilt", StringComparison.Ordinal))
        {
            return;
        }

        if (!sanitized.HasChanges)
        {
            FuzzAssert.True(sanitized.Output.Span.SequenceEqual(bytes), "The sanitizer changed a file it did not repair");
            return;
        }

        FuzzAssert.True(sanitized.Output.Span.StartsWith(bytes), "The sanitizer did not keep the bytes of the file");
        FuzzAssert.False(PdfSaveSanitizer.Sanitize(sanitized.Output, ReadOptions).HasChanges, "The output of the sanitizer still needs repairs");
    }

    private static IEnumerable<FuzzSeed> GetSeeds(string extension)
    {
        var assembly = typeof(PdfFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(extension, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name, memory.ToArray());
        }
    }
}
