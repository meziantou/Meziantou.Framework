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

    private static readonly string[] FlowLayoutTokens = ["\n", "# ", "## ", "- ", "|", "---", " ", "W"];

    private static readonly Rendering.PdfRenderOptions RenderOptions = new() { Dpi = 18, MaxOperations = 200_000, UseSystemFonts = false };

    private static readonly string[] HeaderFooterTokens = ["{page}", "{pages}", "{label}", "{date}", "{bates}", "{{", "}}", "{", "}", "Page ", " of "];

    private static readonly string[] WatermarkTokens = ["DRAFT", "CONFIDENTIAL", "COPY", "TOP SECRET", " ", "€", "é", "Ā", "中", "\0", "\t", "\n", "W"];

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
        new("pdf-http-range", HttpRange) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-optimizer", Optimizer) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-annotation-appearances", AnnotationAppearances) { Seeds = GetAppearanceSeeds, Dictionary = AppearanceTokens },
        new("pdf-annotation-model", AnnotationModel) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-document-views", DocumentViews) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-separations", Separations) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-compare", Compare) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-table", Table) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
        new("pdf-header-footer", HeaderFooter) { Seeds = GetHeaderFooterSeeds, Dictionary = HeaderFooterTokens },
        new("pdf-watermark", Watermark) { Seeds = GetWatermarkSeeds, Dictionary = WatermarkTokens },
        new("pdf-flow-layout", FlowLayout) { Seeds = GetFlowLayoutSeeds, Dictionary = FlowLayoutTokens },
        new("pdf-auto-tag", AutoTag) { Seeds = () => GetSeeds(".pdf"), Dictionary = PdfTokens },
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
    /// The input is a PDF file, opened from memory and over HTTP with range requests (a server of the bytes of the input).
    /// Both read the same document: the same pages and the same text of the first page after a prefetch, or both throw a
    /// documented exception.
    /// </summary>
    private static void HttpRange(ReadOnlySpan<byte> data)
    {
        var file = data.ToArray();
        var options = new PdfReadOptions { MaxDecodedStreamLength = 1 << 20 };
        (int Pages, string Text)? expected;
        try
        {
            var local = PdfDocument.Open(file, options);
            expected = (local.Pages.Count, local.Pages.Count > 0 ? local.Pages[0].ExtractText() : "");
        }
        catch (PdfException)
        {
            expected = null;
        }

        var (actual, servedBytes) = ReadOverHttpAsync(file, options).GetAwaiter().GetResult();
        FuzzAssert.Equal(expected, actual, "The document read over HTTP is not the document read from memory");
        FuzzAssert.True(servedBytes <= (2L * file.Length) + (64 * 1024), "The bytes of the file are downloaded more than once");
    }

    private static async Task<((int Pages, string Text)? Result, long ServedBytes)> ReadOverHttpAsync(byte[] file, PdfReadOptions options)
    {
        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);
        try
        {
            var remote = await PdfDocument.OpenAsync(client, new Uri("https://localhost/file.pdf"), options).ConfigureAwait(false);
            if (remote.Pages.Count > 0)
            {
                await remote.PrefetchPageAsync(0).ConfigureAwait(false);
            }

            return ((remote.Pages.Count, remote.Pages.Count > 0 ? remote.Pages[0].ExtractText() : ""), handler.ServedBytes);
        }
        catch (PdfException)
        {
            return (null, handler.ServedBytes);
        }
    }

    /// <summary>Serves a file with range requests (RFC 9110, 14).</summary>
    private sealed class RangeHandler(byte[] file) : HttpMessageHandler
    {
        public long ServedBytes { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => Serve(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Serve(request));

        private HttpResponseMessage Serve(HttpRequestMessage request)
        {
            if (file.Length == 0 || request.Headers.Range is not { Ranges.Count: 1 } range || range.Ranges.First() is not { From: { } from, To: { } to } || from >= file.Length)
            {
                // An unsatisfiable range gives the length of the file (RFC 9110, 15.5.17)
                var empty = new ByteArrayContent([]);
                empty.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(file.Length);
                return new HttpResponseMessage(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable) { Content = empty };
            }

            to = Math.Min(to, file.Length - 1);
            ServedBytes += to - from + 1;
            var content = new ByteArrayContent(file, (int)from, (int)(to - from + 1));
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, file.Length);
            return new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = content };
        }
    }

    /// <summary>
    /// The input is a PDF file. The typed views of its annotations read within their bounds (the opacity, the border, the
    /// rotation of the widgets); their appearances are drawn; flattening every annotation leaves only the pop-ups of the
    /// annotations of other pages and the widgets of signed signatures, the redactions are applied, and the document
    /// reads back.
    /// </summary>
    private static void AnnotationModel(ReadOnlySpan<byte> data)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(data.ToArray(), new PdfReadOptions { MaxDecodedStreamLength = 1 << 20 });
            if (document.Pages.Count == 0)
                return;
        }
        catch (PdfException)
        {
            return;
        }

        try
        {
            foreach (var page in document.Pages.Take(4))
            {
                foreach (var annotation in page.Annotations.Take(64))
                {
                    ReadAnnotation(annotation);
                    _ = annotation.UpdateAppearance();
                }
            }

            foreach (var page in document.Pages.Take(4))
            {
                page.FlattenAnnotations(new PdfAnnotationFlattenOptions { Filter = annotation => annotation is not PdfRedactAnnotation });
                foreach (var annotation in page.Annotations)
                {
                    FuzzAssert.True(annotation is PdfPopupAnnotation or PdfWidgetAnnotation or PdfRedactAnnotation, $"A {annotation.Subtype} annotation was not flattened");
                }
            }

            document.ApplyRedactions();
            FuzzAssert.False(document.Pages.Take(4).Any(page => page.Annotations.OfType<PdfRedactAnnotation>().Any()), "A redaction annotation was not applied");
            var read = PdfDocument.Open(document.ToByteArray());
            FuzzAssert.Equal(document.Pages.Count, read.Pages.Count, "The pages of the flattened document did not read back");
        }
        catch (PdfException)
        {
        }
    }

    /// <summary>
    /// The input is a PDF file. The typed views of the document read within their bounds: the actions and their
    /// <c>/Next</c> chains, the additional actions, the optional content, the viewer preferences, the portfolio and its
    /// folders, and the revisions, which are prefixes of the file in increasing order that open as documents.
    /// </summary>
    private static void DocumentViews(ReadOnlySpan<byte> data)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(data.ToArray(), new PdfReadOptions { MaxDecodedStreamLength = 1 << 20 });
        }
        catch (PdfException)
        {
            return;
        }

        try
        {
            ReadAction(document.OpenAction, depth: 0);
            foreach (var trigger in document.AdditionalActions.Triggers)
            {
                ReadAction(document.AdditionalActions[trigger], depth: 0);
            }

            foreach (var page in document.Pages.Take(4))
            {
                foreach (var trigger in page.AdditionalActions.Triggers)
                {
                    ReadAction(page.AdditionalActions[trigger], depth: 0);
                }

                foreach (var annotation in page.Annotations.Take(64))
                {
                    _ = annotation.OptionalContent;
                    ReadAction((annotation as PdfLinkAnnotation)?.Action ?? (annotation as PdfWidgetAnnotation)?.Action, depth: 0);
                    foreach (var trigger in annotation.AdditionalActions.Triggers)
                    {
                        ReadAction(annotation.AdditionalActions[trigger], depth: 0);
                    }
                }
            }

            foreach (var item in document.Outlines.Take(64))
            {
                ReadAction(item.Action, depth: 0);
            }

            var content = document.OptionalContent;
            foreach (var group in content.Groups.Take(64))
            {
                _ = (group.Name, group.Intents, group.IsVisible);
            }

            if (content.DefaultConfiguration is { } configuration)
            {
                _ = (configuration.BaseState, configuration.LockedGroups, configuration.RadioButtonGroups, configuration.Name);
            }

            FuzzAssert.True(content.Configurations.Count <= 4096, "Too many optional content configurations");
            var preferences = document.ViewerPreferences;
            _ = (preferences.HideToolbar, preferences.DisplayDocumentTitle, preferences.NonFullScreenPageMode, preferences.Direction, preferences.Duplex, preferences.PickTrayByPdfSize, preferences.EnforcedPreferences);
            FuzzAssert.True(preferences.PrintPageRanges.All(range => range.First >= 0 && range.Last >= range.First), "A print page range is not in order");
            FuzzAssert.True(preferences.NumberOfCopies is null or (>= 1 and <= 5), "The number of copies is out of range");
            _ = (document.PageLayout, document.PageMode);

            if (document.Portfolio is { } portfolio)
            {
                _ = (portfolio.View, portfolio.InitialFile, portfolio.SortFields, portfolio.SortAscending);
                foreach (var field in portfolio.Fields)
                {
                    _ = (field.Name, field.Type, field.Order, field.IsVisible, field.IsEditable);
                }

                var pending = new Stack<(PdfPortfolioFolder Folder, int Depth)>();
                if (portfolio.RootFolder is { } root)
                {
                    pending.Push((root, 0));
                }

                var count = 0;
                while (pending.Count > 0 && count++ < 10_000)
                {
                    var (folder, depth) = pending.Pop();
                    FuzzAssert.True(depth <= 32, "The folders of a portfolio are nested too deeply");
                    _ = (folder.Name, folder.Id);
                    foreach (var child in folder.Children.Take(16))
                    {
                        pending.Push((child, depth + 1));
                    }
                }
            }

            var revisions = document.Revisions;
            for (var i = 0; i < revisions.Count; i++)
            {
                FuzzAssert.True(revisions[i].Length > 0 && revisions[i].Length <= data.Length, "A revision is not a prefix of the file");
                FuzzAssert.True(i == 0 || revisions[i].Length > revisions[i - 1].Length, "The revisions are not in increasing order");
                FuzzAssert.Equal(i == revisions.Count - 1, revisions[i].IsLatest, "Only the last revision is the latest");
            }

            foreach (var revision in revisions.Take(3))
            {
                try
                {
                    _ = revision.Open().Pages.Count;
                }
                catch (PdfException)
                {
                }
            }
        }
        catch (PdfException)
        {
        }
    }

    private static void ReadAction(PdfAction? action, int depth)
    {
        if (action is null || depth > 4)
            return;

        _ = action.Kind;
        switch (action)
        {
            case PdfGoToAction goTo:
                _ = goTo.Destination;
                break;
            case PdfGoToRemoteAction remote:
                _ = (remote.FileName, remote.PageIndex, remote.DestinationName, remote.NewWindow);
                break;
            case PdfGoToEmbeddedAction embedded:
                _ = (embedded.FileName, embedded.TargetFileName);
                break;
            case PdfLaunchAction launch:
                _ = launch.FileName;
                break;
            case PdfUriAction uri:
                _ = (uri.Uri, uri.IsMap);
                break;
            case PdfNamedAction named:
                _ = named.Name;
                break;
            case PdfSubmitFormAction submit:
                _ = (submit.Url, submit.FieldNames, submit.Flags);
                break;
            case PdfResetFormAction reset:
                _ = (reset.FieldNames, reset.Exclude);
                break;
            case PdfImportDataAction import:
                _ = import.FileName;
                break;
            case PdfHideAction hide:
                _ = (hide.FieldNames, hide.Hides);
                break;
            case PdfSetOptionalContentStateAction state:
                _ = (state.Changes, state.PreserveRadioButtons);
                break;
            case PdfJavaScriptAction script:
                _ = script.Script;
                break;
        }

        var next = action.Next;
        FuzzAssert.True(next.Count <= 256, "Too many actions after an action");
        foreach (var item in next.Take(4))
        {
            ReadAction(item, depth + 1);
        }
    }

    private static void ReadAnnotation(PdfAnnotation annotation)
    {
        _ = annotation.Rectangle;
        _ = annotation.Contents;
        _ = annotation.UniqueName;
        _ = annotation.ModificationDate;
        _ = annotation.Color;
        _ = annotation.HasAppearance;
        var border = annotation.Border;
        FuzzAssert.True(border.Width >= 0 && double.IsFinite(border.Width), "The width of a border is not a positive number");
        FuzzAssert.True(border.CloudyIntensity is >= 0 and <= 2, "The intensity of a cloudy border is out of range");
        FuzzAssert.True(border.DashPattern.Count is > 0 and <= 16 && border.DashPattern.All(length => length >= 0), "The dashes of a border are out of range");
        _ = border.BorderStyle;
        if (annotation is PdfMarkupAnnotation markup)
        {
            FuzzAssert.True(markup.Opacity is >= 0 and <= 1, "The opacity of an annotation is out of range");
            _ = markup.Popup;
            _ = markup.InReplyTo;
            _ = markup.ReplyType;
            _ = markup.RichContents;
            _ = markup.GetReplies();
        }

        switch (annotation)
        {
            case PdfTextAnnotation text:
                _ = (text.Icon, text.IsOpen, text.State, text.StateModel);
                break;
            case PdfFreeTextAnnotation freeText:
                _ = (freeText.DefaultAppearance, freeText.Alignment, freeText.CalloutLineEnding, freeText.Intent);
                FuzzAssert.True(freeText.CalloutLine.Count is 0 or 2 or 3, "A callout line does not have 2 or 3 points");
                break;
            case PdfLineAnnotation line:
                _ = (line.Start, line.End, line.StartEnding, line.EndEnding, line.InteriorColor, line.LeaderLineLength, line.LeaderLineExtension, line.ShowCaption);
                break;
            case PdfSquareAnnotation square:
                _ = square.InteriorColor;
                break;
            case PdfCircleAnnotation circle:
                _ = circle.InteriorColor;
                break;
            case PdfPolygonAnnotation polygon:
                FuzzAssert.True(polygon.Vertices.Count <= 65536, "Too many vertices");
                _ = polygon.InteriorColor;
                break;
            case PdfPolyLineAnnotation polyline:
                FuzzAssert.True(polyline.Vertices.Count <= 65536, "Too many vertices");
                _ = (polyline.StartEnding, polyline.EndEnding, polyline.InteriorColor);
                break;
            case PdfInkAnnotation ink:
                FuzzAssert.True(ink.Strokes.Sum(stroke => stroke.Count) <= 65536, "Too many points in the strokes");
                break;
            case PdfTextMarkupAnnotation textMarkup:
                _ = (textMarkup.Kind, textMarkup.Regions);
                break;
            case PdfStampAnnotation stamp:
                FuzzAssert.False(string.IsNullOrEmpty(stamp.Icon), "A stamp has no name");
                break;
            case PdfCaretAnnotation caret:
                _ = caret.ShowsParagraphSymbol;
                break;
            case PdfRedactAnnotation redact:
                FuzzAssert.True(redact.Regions.Count > 0, "A redaction has no region");
                _ = (redact.InteriorColor, redact.OverlayText, redact.RepeatOverlayText);
                break;
            case PdfPopupAnnotation popup:
                _ = (popup.Parent, popup.IsOpen);
                break;
            case PdfWidgetAnnotation widget:
                var characteristics = widget.AppearanceCharacteristics;
                FuzzAssert.True(characteristics.Rotation is 0 or 90 or 180 or 270, "The rotation of a widget is not a multiple of 90");
                _ = (widget.AppearanceState, widget.HighlightMode, characteristics.BorderColor, characteristics.BackgroundColor, characteristics.NormalCaption, characteristics.RolloverCaption, characteristics.DownCaption);
                break;
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

    /// <summary>
    /// The input is seven option bytes, then the text of a watermark added to a document of three pages rotated by 0, 90
    /// and 180 degrees: the position, the tiling and the placement; the first page; the last page (255 for none); the
    /// margin; the font size (0 for none); the spacing of the tiles (0 for none); the rotation (0 for none). The watermark
    /// either throws <see cref="ArgumentException"/> and leaves the document unchanged, or writes a document that reads
    /// back with the same pages, the pages outside the range unchanged and those of the range marked.
    /// </summary>
    private static void Watermark(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
            return;

        var text = FuzzText.Decode(data[7..]);
        var document = new PdfDocument();
        for (var i = 0; i < 3; i++)
        {
            document.Pages.Add(PdfSize.A5).Rotation = i * 90;
        }

        var before = document.ToByteArray();
        var contents = document.Pages.Select(page => page.GetContentData()).ToArray();
        PdfWatermarkOptions options;
        try
        {
            // The negative values of the margin and the spacing are invalid
            options = new PdfWatermarkOptions
            {
                Position = (PdfWatermarkPosition)((data[0] & 0x0F) % 10),
                IsTiled = (data[0] & 0x10) != 0,
                Placement = (data[0] & 0x20) != 0 ? PdfContentPlacement.Under : PdfContentPlacement.Over,
                FirstPageIndex = data[1] & 3,
                LastPageIndex = data[2] == 0xFF ? null : data[2] & 3,
                Margin = (sbyte)data[3],
                FontSize = data[4] == 0 ? null : data[4] / 2.0,
                TileSpacing = data[5] == 0 ? null : (sbyte)data[5],
                Rotation = data[6] == 0 ? null : (sbyte)data[6] * 3,
            };
            document.AddWatermark(text, options);
        }
        catch (ArgumentException)
        {
            FuzzAssert.True(document.ToByteArray().AsSpan().SequenceEqual(before), "A rejected watermark changed the document");
            return;
        }

        var read = PdfDocument.Open(document.ToByteArray());
        FuzzAssert.Equal(3, read.Pages.Count, "The watermarked document lost pages");
        var last = Math.Min(options.LastPageIndex ?? 2, 2);
        for (var i = 0; i < 3; i++)
        {
            var content = read.Pages[i].GetContentData();
            if (i < options.FirstPageIndex || i > last)
            {
                FuzzAssert.True(content.AsSpan().SequenceEqual(contents[i]), "A page outside the range changed");
            }
            else if (!options.IsTiled)
            {
                // A copy that is not tiled is always painted; the steps of a tiled one can be too small to be written
                FuzzAssert.True(System.Text.Encoding.Latin1.GetString(content).Contains("/Artifact", StringComparison.Ordinal), "A page of the range has no watermark");
            }
        }
    }

    /// <summary>
    /// The input is an option byte (the page size, the alignment, the columns), then a text whose lines are blocks: a
    /// heading (<c>#</c>), a list item (<c>-</c>), a table row (<c>|</c>), a rule (<c>---</c>), or a paragraph. The text is
    /// laid out without an exception, every line of every page is inside the margins, and the document reads back with
    /// its tags.
    /// </summary>
    private static void FlowLayout(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var options = data[0];
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

        // The characters of the standard font: the others are an argument error of the caller
        var text = new string([.. FuzzText.Decode(data[1..]).Take(4000).Where(c => c is '\n' || (c >= ' ' && font.CanEncode(c.ToString())))]);
        var size = (options & 1) == 0 ? new PdfSize(200, 220) : PdfSize.A5;
        var alignment = (PdfTextAlignment)((options >> 1) & 3);
        var flow = new PdfFlowDocument(new PdfFlowOptions { PageSize = size, MarginLeft = 20, MarginRight = 20, MarginTop = 20, MarginBottom = 20, Footer = (page, count) => $"{page}/{count}" });
        var style = new PdfParagraphStyle { Alignment = alignment };
        void AddBlocks(PdfFlowContainer container, IEnumerable<string> lines)
        {
            var items = new List<string>();
            var rows = new List<string[]>();
            void Flush()
            {
                if (items.Count > 0)
                {
                    container.List([.. items], ordered: (options & 8) != 0);
                    items.Clear();
                }

                if (rows.Count > 0)
                {
                    container.Table(table =>
                    {
                        // The first row gives the columns; the cells of a longer row do not fit
                        table.HeaderRow(rows[0]);
                        foreach (var row in rows.Skip(1))
                        {
                            table.Row(cells =>
                            {
                                for (var i = 0; i < row.Length; i++)
                                {
                                    cells.Cell(row[i], rowSpan: i == 0 && row.Length < rows[0].Length ? 2 : 1);
                                }
                            });
                        }
                    });
                    rows.Clear();
                }
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    items.Add(line[2..]);
                    continue;
                }

                if (line.StartsWith('|', StringComparison.Ordinal))
                {
                    rows.Add(line.Split('|', StringSplitOptions.RemoveEmptyEntries).Take(8).DefaultIfEmpty("").ToArray());
                    continue;
                }

                Flush();
                if (line == "---")
                {
                    container.Rule();
                }
                else if (line.StartsWith('#', StringComparison.Ordinal))
                {
                    container.Heading(Math.Clamp(line.TakeWhile(c => c == '#').Count(), 1, 6), line.TrimStart('#'));
                }
                else
                {
                    container.Paragraph(line, style);
                }
            }

            Flush();
        }

        var lines = text.Split('\n');
        if ((options & 16) != 0)
        {
            flow.TableOfContents();
        }

        if ((options & 32) != 0)
        {
            flow.Columns(2, 10, columns => AddBlocks(columns, lines));
        }
        else
        {
            AddBlocks(flow, lines);
        }

        PdfDocument document;
        try
        {
            document = flow.Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("do not fit", StringComparison.Ordinal))
        {
            return;
        }

        var read = PdfDocument.Open(document.ToByteArray());
        FuzzAssert.Equal(document.Pages.Count, read.Pages.Count, "The pages of the flow document did not read back");
        FuzzAssert.True(read.StructureTree.Exists, "The flow document is not tagged");
        foreach (var page in read.Pages.Take(16))
        {
            foreach (var line in page.ExtractTextLayout().Lines)
            {
                // The glyphs of a line can go a little beyond its box (accents, the footer in the margin)
                FuzzAssert.True(line.Bounds.Left >= 19 && line.Bounds.Right <= size.Width - 19, $"A line is outside of the margins: {line.Text} {line.Bounds}");
                FuzzAssert.True(line.Bounds.Bottom >= 0 && line.Bounds.Top <= size.Height, $"A line is outside of the page: {line.Text} {line.Bounds}");
            }
        }
    }

    /// <summary>
    /// The input is a PDF file. The layout of its pages has nodes in bounds, and tagging the document does not change what
    /// its first page shows (only marked-content operators are added): the rendering is the same, the document reads back
    /// with a structure tree, and it cannot be tagged twice.
    /// </summary>
    private static void AutoTag(ReadOnlySpan<byte> data)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(data.ToArray(), new PdfReadOptions { MaxDecodedStreamLength = 1 << 20 });
            if (document.Pages.Count is 0 or > 8 || document.StructureTree.Exists)
                return;

            foreach (var page in document.ExtractLayout())
            {
                foreach (var node in page.Nodes)
                {
                    FuzzAssert.True(double.IsFinite(node.Bounds.Width) && node.Bounds.Width >= 0, $"The bounds of a {node.Kind} are not valid");
                    FuzzAssert.True(node.Kind != PdfLayoutNodeKind.Heading || node.Level is >= 1 and <= 6, "The level of a heading is out of range");
                }
            }
        }
        catch (PdfException)
        {
            return;
        }

        byte[] before;
        try
        {
            using var image = Rendering.PdfRenderer.RenderPage(document.Pages[0], RenderOptions);
            before = Pixels(image);
        }
        catch (PdfException)
        {
            return;
        }

        try
        {
            document.AutoTag();
        }
        catch (PdfException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            // Already marked as tagged
            return;
        }

        var read = PdfDocument.Open(document.ToByteArray(), new PdfReadOptions { MaxDecodedStreamLength = 1 << 20 });
        FuzzAssert.True(read.StructureTree.Exists, "The tagged document has no structure tree");
        using (var image = Rendering.PdfRenderer.RenderPage(read.Pages[0], RenderOptions))
        {
            FuzzAssert.True(Pixels(image).AsSpan().SequenceEqual(before), "Tagging the document changed what its first page shows");
        }

        var tagged = false;
        try
        {
            read.AutoTag();
        }
        catch (InvalidOperationException)
        {
            tagged = true;
        }

        FuzzAssert.True(tagged, "A tagged document was tagged again");
    }

    private static byte[] Pixels(Imaging.Image<Imaging.Rgba32> image)
    {
        var result = new byte[image.Width * image.Height * 4];
        var index = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                result[index++] = pixel.R;
                result[index++] = pixel.G;
                result[index++] = pixel.B;
                result[index++] = pixel.A;
            }
        }

        return result;
    }

    private static IEnumerable<FuzzSeed> GetFlowLayoutSeeds()
    {
        yield return FuzzSeed.FromText("document", [0x11], "# Title\nA paragraph of text that is long enough to be broken into several lines of the page.\n- One\n- Two\n|a|b\n|c|d\n|e\n---\n## Section\nEnd.");
        yield return FuzzSeed.FromText("columns", [0x26], "# Columns\nThe quick brown fox jumps over the lazy dog.\nThe quick brown fox jumps over the lazy dog.\n- item");
        yield return FuzzSeed.FromText("long-word", [0x00], new string('W', 200));
    }

    private static IEnumerable<FuzzSeed> GetHeaderFooterSeeds()
    {
        yield return FuzzSeed.FromText("page", "Page {page} of {pages}");
        yield return FuzzSeed.FromText("date", "{date} {{label}} {label}");
        yield return FuzzSeed.FromText("bates", "{bates}");
        yield return FuzzSeed.FromText("invalid", "{unknown} }");
    }

    private static IEnumerable<FuzzSeed> GetWatermarkSeeds()
    {
        // The options of the tests of PdfPageTests: option bytes, then the text
        yield return FuzzSeed.FromText("centered", [0x00, 0x00, 0xFF, 36, 0, 0, 0], "DRAFT");
        yield return FuzzSeed.FromText("corner", [(byte)PdfWatermarkPosition.TopRight, 0x00, 0xFF, 36, 48, 0, 0], "CONFIDENTIAL");
        yield return FuzzSeed.FromText("rotated-corner", [(byte)PdfWatermarkPosition.BottomLeft, 0x01, 0x01, 20, 40, 0, 0], "COPY");
        yield return FuzzSeed.FromText("range", [0x00, 0x01, 0x02, 36, 0, 0, 0], "DRAFT");
        yield return FuzzSeed.FromText("tiled", [0x10, 0x00, 0xFF, 36, 36, 12, 0], "CONFIDENTIAL");
        yield return FuzzSeed.FromText("tiled-under", [0x30, 0x00, 0xFF, 36, 0, 0, 0], "DRAFT");
        yield return FuzzSeed.FromText("rotation", [0x20, 0x00, 0xFF, 36, 40, 0, 30], "TOP SECRET");
        yield return FuzzSeed.FromText("invalid-margin", [(byte)PdfWatermarkPosition.Left, 0x00, 0xFF, 0xFF, 0, 0, 0], "DRAFT");
        yield return FuzzSeed.FromText("invalid-spacing", [0x10, 0x00, 0xFF, 36, 0, 0x80, 0], "DRAFT");
        yield return FuzzSeed.FromText("invalid-text", [0x00, 0x00, 0xFF, 36, 0, 0, 0], "中");
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
