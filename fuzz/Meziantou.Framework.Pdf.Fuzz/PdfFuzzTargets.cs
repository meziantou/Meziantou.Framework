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

    private static readonly string[] AppearanceTokens =
    [
        "<body>", "</body>", "<p>", "</p>", "<b>", "</b>", "<i>", "</i>", "<span style=\"", "</span>", "<br/>", "<div>", "</div>",
        "font-size:", "font-weight:bold", "font-style:italic", "font-family:", "font:", "color:#", "color:rgb(", "text-align:center", "text-align:right",
        "pt", "px", ";", "Times", "Courier", "serif", "monospace", "&amp;", "<![CDATA[", "<?xml version=\"1.0\"?>",
        "None", "Square", "Circle", "Diamond", "OpenArrow", "ClosedArrow", "Butt", "ROpenArrow", "RClosedArrow", "Slash",
    ];

    private static readonly string[] LineEndings = ["None", "Square", "Circle", "Diamond", "OpenArrow", "ClosedArrow", "Butt", "ROpenArrow", "RClosedArrow", "Slash"];

    private static readonly Rendering.PdfRenderOptions RenderOptions = new() { Dpi = 18, MaxOperations = 200_000, UseSystemFonts = false };

    private static readonly string[] HeaderFooterTokens = ["{page}", "{pages}", "{label}", "{date}", "{bates}", "{{", "}}", "{", "}", "Page ", " of "];

    private static readonly string[] FormScriptTokens =
    [
        "event.value", "event.rc = false;", "event.target", "event.source", "this.getField(\"A\")", "this.getField(\"B\").value", ".value = ", ".valueAsString",
        "AFSimple_Calculate(\"SUM\", \"A, B\");", "AFNumber_Format(2, 0, 0, 0, \"$\", true);", "AFNumber_Keystroke(2, 0, 0, 0, \"\", true);", "AFPercent_Format(1, 0);",
        "AFDate_FormatEx(\"yyyy-mm-dd\");", "AFDate_KeystrokeEx(\"mm/dd/yyyy\");", "AFTime_Format(1);", "AFSpecial_Format(2);", "AFRange_Validate(true, 0, true, 10);",
        "util.printf(\"%,0.2f\", 1)", "util.printd(\"mmmm d\", new Date())", "util.scand(\"yyyy\", \"2026\")", "app.alert(\"x\")", "app.launchURL(\"u\")", "this.submitForm()",
        "while (true) {}", "function f() { f(); } f();", "new Array(1e9)", "throw 1;", "\u0000", "\n",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("pdf-fdf", Fdf) { Seeds = () => GetSeeds(".fdf"), Dictionary = FdfTokens },
        new("pdf-xfdf", Xfdf) { Seeds = () => GetSeeds(".xfdf"), Dictionary = XfdfTokens },
        new("pdf-round-trip", RoundTrip) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-diagnostics", Diagnostics) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-optimizer", Optimizer) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-annotation-appearances", AnnotationAppearances) { Seeds = GetAppearanceSeeds, Dictionary = AppearanceTokens },
        new("pdf-separations", Separations) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-compare", Compare) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-table", Table) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-header-footer", HeaderFooter) { Seeds = GetHeaderFooterSeeds, Dictionary = HeaderFooterTokens },
        new("pdf-accessibility", Accessibility) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-imposition", Imposition) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-script-extraction", ScriptExtraction) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-form-scripts", FormScripts) { Seeds = GetFormScriptSeeds, Dictionary = FormScriptTokens },
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

    /// <summary>
    /// The input is a PDF file, opened with all its objects read. The diagnostics are within their bounds: a repaired file
    /// reports that its cross-reference information was rebuilt, the offsets are in the file, the object numbers are
    /// positive, the list is bounded, and reading the file again adds nothing.
    /// </summary>
    private static void Diagnostics(ReadOnlySpan<byte> data)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(data.ToArray(), new PdfReadOptions { LoadAllObjects = true, MaxDecodedStreamLength = 1 << 20 });
        }
        catch (PdfException)
        {
            return;
        }

        var diagnostics = document.Diagnostics;
        FuzzAssert.True(diagnostics.Count <= 1000, "The number of diagnostics is not bounded");
        FuzzAssert.Equal(document.IsRepaired, diagnostics.Any(diagnostic => diagnostic.Kind == PdfDiagnosticKind.CrossReferenceRebuilt), "A repaired file must report that its cross-reference information was rebuilt, and only a repaired file");
        foreach (var diagnostic in diagnostics)
        {
            FuzzAssert.True(diagnostic.Offset is not { } offset || (offset >= 0 && offset <= data.Length), $"The offset of a diagnostic is outside of the file: {diagnostic}");
            FuzzAssert.True(diagnostic.ObjectNumber is null or >= 0, $"The object number of a diagnostic is negative: {diagnostic}");
            FuzzAssert.False(string.IsNullOrEmpty(diagnostic.ToString()), "A diagnostic has no text");
        }

        try
        {
            FuzzAssert.Equal(diagnostics.Count, document.ValidateStructure().Count, "Reading the file again reports new problems");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is two option bytes (the flags of a text field, the line endings), then a rich text value. The page has a
    /// text field whose value is drawn from it (rich text, comb or plain), and lines and polylines with endings, without
    /// appearances: the renderer generates them without throwing.
    /// </summary>
    private static void AnnotationAppearances(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return;

        var options = data[0];
        var endings = data[1];
        var text = FuzzText.Decode(data[2..]);
        var flags = ((options & 1) != 0 ? 1 << 25 : 0) | ((options & 2) != 0 ? 1 << 24 : 0) | ((options & 4) != 0 ? 1 << 12 : 0);
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var annotations = new Objects.PdfArray();
        page.Dictionary[Objects.PdfName.Create("Annots")] = annotations;
        var widget = new Objects.PdfDictionary
        {
            [Objects.PdfName.Create("Type")] = Objects.PdfName.Create("Annot"),
            [Objects.PdfName.Create("Subtype")] = Objects.PdfName.Create("Widget"),
            [Objects.PdfName.Create("FT")] = Objects.PdfName.Create("Tx"),
            [Objects.PdfName.Create("T")] = Objects.PdfString.FromText("field"),
            [Objects.PdfName.Create("Ff")] = Objects.PdfInteger.Get(flags),
            [Objects.PdfName.Create("MaxLen")] = Objects.PdfInteger.Get((options >> 3) + 1),
            [Objects.PdfName.Create("V")] = Objects.PdfString.FromText(text),
            [Objects.PdfName.Create("RV")] = Objects.PdfString.FromText(text),
            [Objects.PdfName.Create("DS")] = Objects.PdfString.FromText("font-size:9pt"),
            [Objects.PdfName.Create("DA")] = Objects.PdfString.FromText("/Helv 0 Tf 0 g"),
            [Objects.PdfName.Create("Rect")] = Objects.PdfArray.FromNumbers(10, 100, 190, 190),
        };
        annotations.Add(document.Objects.Add(widget));
        var lineEndings = new Objects.PdfArray(Objects.PdfName.Create(LineEndings[endings % 10]), Objects.PdfName.Create(LineEndings[endings / 10 % 10]));
        foreach (var (subtype, key, points) in new[] { ("Line", "L", new double[] { 20, 20, 180, 80 }), ("PolyLine", "Vertices", new double[] { 20, 90, 100, 20, 100, 20, 180, 90 }) })
        {
            annotations.Add(document.Objects.Add(new Objects.PdfDictionary
            {
                [Objects.PdfName.Create("Type")] = Objects.PdfName.Create("Annot"),
                [Objects.PdfName.Create("Subtype")] = Objects.PdfName.Create(subtype),
                [Objects.PdfName.Create(key)] = Objects.PdfArray.FromNumbers(points),
                [Objects.PdfName.Create("LE")] = lineEndings,
                [Objects.PdfName.Create("C")] = Objects.PdfArray.FromNumbers(0, 0, 1),
                [Objects.PdfName.Create("IC")] = Objects.PdfArray.FromNumbers(1, 0, 0),
                [Objects.PdfName.Create("Rect")] = Objects.PdfArray.FromNumbers(0, 0, 200, 100),
            }));
        }

        try
        {
            using var image = Rendering.PdfRenderer.RenderPage(PdfDocument.Open(document.ToByteArray()).Pages[0], RenderOptions);
            FuzzAssert.Equal(50, image.Width, "The page was not rendered at the resolution of the options");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is a PDF file. The colorants of its first page, its print production report, the plate of each colorant
    /// (gray) and the overprint simulation (opaque) are computed without throwing other exceptions than <see cref="PdfException"/>.
    /// </summary>
    private static void Separations(ReadOnlySpan<byte> data)
    {
        try
        {
            var document = PdfDocument.Open(data.ToArray(), ReadOptions);
            var report = PdfPrintProductionReport.Create(document);
            FuzzAssert.Equal(document.Pages.Count, report.Pages.Count, "The report does not have every page");
            if (document.Pages.Count == 0)
                return;

            var page = document.Pages[0];
            var colorants = PdfSeparations.GetColorants(page);
            FuzzAssert.True(colorants.ProcessColorants.All(colorant => colorant is "Cyan" or "Magenta" or "Yellow" or "Black"), "A process colorant is not a process colorant");
            foreach (var colorant in colorants.Colorants.Take(6))
            {
                using var plate = Rendering.PdfRenderer.RenderPage(page, new Rendering.PdfRenderOptions { Dpi = 9, MaxOperations = 200_000, UseSystemFonts = false, Plate = colorant });
                var frame = plate.Frames[0];
                for (var y = 0; y < plate.Height; y++)
                {
                    for (var x = 0; x < plate.Width; x++)
                    {
                        var pixel = frame[x, y];
                        FuzzAssert.True(pixel.R == pixel.G && pixel.G == pixel.B, "A plate is not gray");
                    }
                }
            }

            using var preview = Rendering.PdfRenderer.RenderPage(page, new Rendering.PdfRenderOptions { Dpi = 9, MaxOperations = 200_000, UseSystemFonts = false, SimulateOverprint = true });
            FuzzAssert.Equal(255, (int)preview.Frames[0][0, 0].A, "The overprint simulation is not opaque");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is a PDF file: a document compared with itself, and with the document written and read back, shows the
    /// same elements; the diff of an equivalent comparison is empty.
    /// </summary>
    private static void Compare(ReadOnlySpan<byte> data)
    {
        PdfDocument document;
        byte[] written;
        try
        {
            document = PdfDocument.Open(data.ToArray(), ReadOptions);
            if (document.Pages.Count > 8)
                return;

            written = document.ToByteArray();
        }
        catch (PdfException)
        {
            return;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No object number", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var self = PdfDocumentComparer.Compare(document, document);
            FuzzAssert.True(self.AreEquivalent, "A document differs from itself: " + self);
            var rewritten = PdfDocumentComparer.Compare(document, PdfDocument.Open(written, ReadOptions));
            FuzzAssert.True(rewritten.AreEquivalent, "A document differs from the document written: " + rewritten);
            FuzzAssert.Equal("", rewritten.ToString(), "The diff of equivalent documents is not empty");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is a PDF file: the table of the crop box of its first page is rebuilt, or a reason is given. A table has
    /// as many cells in each row as columns, and its boundaries are sorted (left to right, top to bottom).
    /// </summary>
    private static void Table(ReadOnlySpan<byte> data)
    {
        try
        {
            var document = PdfDocument.Open(data.ToArray(), ReadOptions);
            if (document.Pages.Count == 0)
                return;

            var page = document.Pages[0];
            var result = PdfTableExtractor.Extract(page, page.CropBox);
            if (!result.Succeeded)
            {
                FuzzAssert.True(result.Failure is not null && result.Message is not null, "A failure has no reason");
                return;
            }

            var table = result.Table;
            FuzzAssert.True(table.ColumnCount >= 1 && table.RowCount >= 1, "A table has no cell");
            FuzzAssert.True(table.Rows.All(row => row.Count == table.ColumnCount), "A row does not have a cell per column");
            FuzzAssert.True(table.ColumnBoundaries.Zip(table.ColumnBoundaries.Skip(1)).All(pair => pair.First < pair.Second), "The columns are not sorted");
            FuzzAssert.True(table.RowBoundaries.Zip(table.RowBoundaries.Skip(1)).All(pair => pair.First > pair.Second), "The rows are not sorted");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is a template: stamping it as a footer of a document of three pages either throws
    /// <see cref="ArgumentException"/> and leaves the document unchanged, or writes a document whose pages read back.
    /// </summary>
    private static void HeaderFooter(ReadOnlySpan<byte> data)
    {
        var template = FuzzText.Decode(data);
        var document = new PdfDocument();
        for (var i = 0; i < 3; i++)
        {
            document.Pages.Add(PdfSize.A5).Rotation = i * 90;
        }

        var before = document.ToByteArray();
        try
        {
            document.AddHeaderFooter(new PdfHeaderFooterOptions
            {
                FooterCenter = template,
                HeaderRight = template,
                Date = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Bates = new PdfBatesNumbering { Prefix = "X" },
            });
        }
        catch (ArgumentException)
        {
            FuzzAssert.True(document.ToByteArray().AsSpan().SequenceEqual(before), "A rejected template changed the document");
            return;
        }

        FuzzAssert.Equal(3, PdfDocument.Open(document.ToByteArray()).Pages.Count, "The stamped document lost pages");
    }

    private static IEnumerable<FuzzSeed> GetHeaderFooterSeeds()
    {
        yield return FuzzSeed.FromText("page", "Page {page} of {pages}");
        yield return FuzzSeed.FromText("date", "{date} {{label}} {label}");
        yield return FuzzSeed.FromText("bates", "{bates}");
        yield return FuzzSeed.FromText("invalid", "{unknown} }");
    }

    /// <summary>The input is a PDF file: the accessibility inspector reports issues of existing pages only, and does not change the document.</summary>
    private static void Accessibility(ReadOnlySpan<byte> data)
    {
        try
        {
            var document = PdfDocument.Open(data.ToArray(), ReadOptions);
            var report = PdfAccessibilityInspector.Inspect(document);
            foreach (var issue in report.Issues)
            {
                FuzzAssert.True(issue.PageIndex is null || (issue.PageIndex >= 0 && issue.PageIndex < document.Pages.Count), "An issue is on a page the document does not have");
                FuzzAssert.False(string.IsNullOrEmpty(issue.Message), "An issue has no message");
            }

            FuzzAssert.Equal(report.IsTagged, report.Issues.All(issue => issue.Kind != PdfAccessibilityIssueKind.MissingStructureTree), "A tagged document has no structure tree");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is a PDF file: its 2-up sheets have half as many pages, rounded up, and splitting its spreads adds one
    /// page per spread; both documents are written and read back.
    /// </summary>
    private static void Imposition(ReadOnlySpan<byte> data)
    {
        try
        {
            var document = PdfDocument.Open(data.ToArray(), ReadOptions);
            var count = document.Pages.Count;
            if (count > 16)
                return;

            var sheets = PdfImposition.CreateNUp(document, new PdfNUpOptions { Columns = 2, Rows = 1, SheetSize = PdfSize.A4.ToLandscape(), DrawBorders = true });
            FuzzAssert.Equal((count + 1) / 2, sheets.Pages.Count, "The 2-up document does not have one sheet per two pages");
            FuzzAssert.Equal((count + 1) / 2, PdfDocument.Open(sheets.ToByteArray()).Pages.Count, "The 2-up document lost sheets");

            var split = PdfImposition.SplitSpreads(document, new PdfSpreadSplitOptions { OnlyLandscapePages = false });
            FuzzAssert.Equal(count + split, document.Pages.Count, "Splitting the spreads did not add one page per spread");
            FuzzAssert.Equal(count + split, PdfDocument.Open(document.ToByteArray()).Pages.Count, "The split document lost pages");
        }
        catch (PdfException)
        {
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No object number", StringComparison.Ordinal))
        {
        }
    }

    /// <summary>The input is a PDF file: the scripts of its form and of its document are read without throwing other exceptions than <see cref="PdfException"/>.</summary>
    private static void ScriptExtraction(ReadOnlySpan<byte> data)
    {
        try
        {
            var document = PdfDocument.Open(data.ToArray(), ReadOptions);
            foreach (var (name, script) in document.JavaScripts)
            {
                FuzzAssert.True(name is not null && script is not null, "A document script has no name or no text");
            }

            var fields = document.Form.GetFields();
            foreach (var field in fields.Take(100))
            {
                var actions = field.Actions;
                _ = actions.Keystroke?.Script.Length + actions.Format?.Script.Length + actions.Validate?.Script.Length + actions.Calculate?.Script.Length;
            }

            var order = document.Form.CalculationOrder;
            FuzzAssert.Equal(order.Count, order.Distinct().Count(), "A field is twice in the calculation order");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is three scripts (the keystroke script of A, the calculation script of B, a document-level script),
    /// separated by NUL characters. A session with tight limits runs them on a form of two fields: no exception of the
    /// host escapes, and the failures of the scripts are diagnostics.
    /// </summary>
    private static void FormScripts(ReadOnlySpan<byte> data)
    {
        var parts = FuzzText.Decode(data).Split('\0');
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A5);
        var a = document.Form.AddTextField(page, "A", new PdfRectangle(10, 10, 100, 30));
        var b = document.Form.AddTextField(page, "B", new PdfRectangle(10, 40, 100, 60), new PdfTextFieldOptions { IsReadOnly = true });
        static Objects.PdfDictionary Script(string text) => new() { [Objects.PdfName.Create("S")] = Objects.PdfName.Create("JavaScript"), [Objects.PdfName.Create("JS")] = Objects.PdfString.FromText(text) };
        a.Dictionary[Objects.PdfName.Create("AA")] = new Objects.PdfDictionary { [Objects.PdfName.Create("K")] = Script(parts[0]), [Objects.PdfName.Create("F")] = Script(parts[0]) };
        b.Dictionary[Objects.PdfName.Create("AA")] = new Objects.PdfDictionary { [Objects.PdfName.Create("C")] = Script(parts.Length > 1 ? parts[1] : "AFSimple_Calculate(\"SUM\", \"A\");") };
        document.Form.Dictionary![Objects.PdfName.Create("CO")] = new Objects.PdfArray(b.Reference!);
        if (parts.Length > 2)
        {
            document.Catalog[Objects.PdfName.Create("Names")] = new Objects.PdfDictionary
            {
                [Objects.PdfName.Create("JavaScript")] = new Objects.PdfDictionary { [Objects.PdfName.Create("Names")] = new Objects.PdfArray(Objects.PdfString.FromText("init"), Script(parts[2])) },
            };
        }

        var options = new JavaScript.PdfFormScriptOptions { Timeout = TimeSpan.FromMilliseconds(250), MaxStatements = 20_000, MemoryLimit = 8 * 1024 * 1024, RecursionLimit = 100 };
        using var session = JavaScript.PdfInteractiveFormScriptExtensions.CreateScriptSession(document.Form, options);
        var result = session.SetValue("A", "12");
        FuzzAssert.True(result.IsAccepted || result.Changes.All(change => change.Field.FullName != "A"), "A rejected value changed the field");
        session.SetValues([new("A", "1,5"), new("B", "x")]);
        session.Recalculate();
        FuzzAssert.Equal(2, PdfDocument.Open(document.ToByteArray()).Form.GetFields().Count, "The form lost fields");
    }

    private static IEnumerable<FuzzSeed> GetFormScriptSeeds()
    {
        yield return FuzzSeed.FromText("af", "AFNumber_Keystroke(2, 0, 0, 0, \"\", true);\0AFSimple_Calculate(\"SUM\", \"A\");\0var rate = 2;");
        yield return FuzzSeed.FromText("custom", "event.value = event.value + '0';\0event.value = this.getField('A').value * rate;\0var rate = 3;");
        yield return FuzzSeed.FromText("reject", "event.rc = false; app.alert('no');\0this.submitForm(); app.launchURL('x');");
        yield return FuzzSeed.FromText("limits", "while (true) {}\0function f() { return f(); } f();\0throw new Error('x');");
        yield return FuzzSeed.FromText("dates", "AFDate_KeystrokeEx('yyyy-mm-dd');\0event.value = util.printd('mmmm d, yyyy', util.scand('yyyy-mm-dd', '2026-03-01')) + util.printf('%,0.2f', 1234.5);");
    }

    private static IEnumerable<FuzzSeed> GetAppearanceSeeds()
    {
        yield return FuzzSeed.FromText("rich-text", [1, 12], "<?xml version=\"1.0\"?><body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Hello <b>bold</b> <span style=\"color:#FF0000;font-style:italic\">red</span></p><p style=\"font:bold 14pt Times\">Title</p></body>");
        yield return FuzzSeed.FromText("rich-text-multiline", [5, 34], "<body><p style=\"text-align:center;font-size:20px\">A<br/>B</p><div style=\"color:rgb(0,128,255);font-family:Courier\">C</div></body>");
        yield return FuzzSeed.FromText("comb", [2 | (5 << 3), 56], "ABCDEF");
        yield return FuzzSeed.FromText("plain", [0, 99], "Plain text");
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
