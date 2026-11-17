using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.Rendering.Colors;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Lists the colorants a page paints (ISO 32000-2, 8.6.6.4): the process colorants (cyan, magenta, yellow, black) and the
/// spot colorants of its separation and DeviceN color spaces. Render the plate of a colorant with
/// <see cref="PdfRenderOptions.Plate"/>, and simulate overprinting with <see cref="PdfRenderOptions.SimulateOverprint"/>.
/// </summary>
public static class PdfSeparations
{
    private const int MaxDepth = 16;
    private const int MaxOperations = 1_000_000;

    private static readonly PdfName CsName = PdfName.Create("CS");
    private static readonly PdfName ImageMaskName = PdfName.Create("ImageMask");
    private static readonly PdfName IMName = PdfName.Create("IM");
    private static readonly PdfName PatternTypeName = PdfName.Create("PatternType");
    private static readonly PdfName AppearanceName = PdfName.Create("AP");

    /// <summary>Lists the colorants and the color spaces a page uses: its content, its forms, images, shadings and patterns, and the appearances of its annotations.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The colorants.</returns>
    /// <remarks>
    /// Gray paints black; RGB and the other color spaces that are not separated (Lab, calibrated RGB, ICC-based RGB) paint
    /// the four process colorants, into which they are converted. The content that cannot be read is skipped.
    /// </remarks>
    public static PdfColorantUsage GetColorants(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var scanner = new Scanner();
        scanner.ScanPage(page);
        return scanner.ToUsage();
    }

    /// <summary>Gets the spot colorants of a page, with the color of their full tint in their alternate color space.</summary>
    internal static List<(string Name, RgbColor Color)> GetSpotColors(PdfPage page)
    {
        var scanner = new Scanner();
        scanner.ScanPage(page);
        return [.. scanner.SpotColors.Select(item => (item.Key, item.Value))];
    }

    private sealed class Scanner
    {
        private readonly HashSet<string> _process = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _families = new(StringComparer.Ordinal);
        private readonly HashSet<PdfObject> _visited = new(ReferenceEqualityComparer.Instance);
        private int _operations;

        public SortedDictionary<string, RgbColor> SpotColors { get; } = new(StringComparer.Ordinal);

        public bool UsesOverprint { get; private set; }

        public PdfColorantUsage ToUsage()
        {
            var process = RenderColorSpace.ProcessColorants.Where(_process.Contains).ToArray();
            return new PdfColorantUsage(process, [.. SpotColors.Keys], [.. _families], UsesOverprint);
        }

        public void ScanPage(PdfPage page)
        {
            using var cache = new RenderCache(new PdfRenderOptions { UseSystemFonts = false });
            try
            {
                ScanContent(page.GetContentData(), page.Resources, cache, depth: 0);
            }
            catch (PdfException)
            {
            }

            foreach (var annotation in page.Annotations)
            {
                try
                {
                    if (annotation.Dictionary.GetResolved(AppearanceName) is PdfDictionary appearances)
                    {
                        switch (appearances.GetResolved(PdfNames.N))
                        {
                            case PdfStream stream:
                                ScanForm(stream, resources: null, cache, depth: 1);
                                break;
                            case PdfDictionary states:
                                foreach (var (key, _) in states)
                                {
                                    if (states.GetResolved(key) is PdfStream state)
                                    {
                                        ScanForm(state, resources: null, cache, depth: 1);
                                    }
                                }

                                break;
                        }
                    }
                }
                catch (PdfException)
                {
                }
            }
        }

        private void ScanForm(PdfStream form, PdfDictionary? resources, RenderCache cache, int depth)
        {
            if (depth > MaxDepth || !_visited.Add(form))
                return;

            ScanContent(form.GetDecodedData(), form.Dictionary.GetResolved(PdfNames.Resources) as PdfDictionary ?? resources, cache, depth);
        }

        private void ScanContent(ReadOnlyMemory<byte> content, PdfDictionary? resources, RenderCache cache, int depth)
        {
            var reader = new ContentStreamReader(content);
            var operands = new List<PdfObject>();
            PdfObject? fillSpace = null;
            PdfObject? strokeSpace = null;
            while (reader.TryRead(operands, out var name) && _operations++ < MaxOperations)
            {
                try
                {
                    switch (name)
                    {
                        // The device colors set by their operators paint the colorants of their nonzero components
                        case "g" or "G":
                            AddDeviceColor("DeviceGray", operands);
                            break;
                        case "rg" or "RG":
                            AddDeviceColor("DeviceRGB", operands);
                            break;
                        case "k" or "K":
                            AddDeviceColor("DeviceCMYK", operands);
                            break;
                        case "cs" or "CS" when operands is [PdfName colorSpace, ..]:
                            var space = ResolveResource(resources, PdfNames.ColorSpace, colorSpace) ?? colorSpace;
                            if (name == "cs")
                            {
                                fillSpace = space;
                            }
                            else
                            {
                                strokeSpace = space;
                            }

                            if (space is PdfName { Value: "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "G" or "RGB" or "CMYK" })
                            {
                                _families.Add(((PdfName)space).Value switch { "G" => "DeviceGray", "RGB" => "DeviceRGB", "CMYK" => "DeviceCMYK", var value => value });
                            }
                            else
                            {
                                AddColorSpace(space, resources, cache);
                            }

                            break;
                        case "sc" or "scn" or "SC" or "SCN" when (name is "sc" or "scn" ? fillSpace : strokeSpace) is PdfName { Value: "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "G" or "RGB" or "CMYK" } device:
                            AddDeviceColor(device.Value switch { "G" => "DeviceGray", "RGB" => "DeviceRGB", "CMYK" => "DeviceCMYK", var value => value }, operands);
                            break;
                        case "scn" or "SCN" when operands is [.., PdfName patternName] && ResolveResource(resources, PdfNames.Pattern, patternName) is { } pattern:
                            ScanPattern(pattern, resources, cache, depth);
                            break;
                        case "sh" when operands is [PdfName shadingName, ..] && ResolveResource(resources, PdfNames.Shading, shadingName) is { } shading:
                            ScanShading(shading, resources, cache);
                            break;
                        case "gs" when operands is [PdfName stateName, ..] && ResolveResource(resources, PdfNames.ExtGState, stateName) is PdfDictionary state:
                            UsesOverprint |= state.GetBoolean(PdfName.Create("OP")) || state.GetBoolean(PdfName.Create("op"));
                            break;
                        case "Do" when operands is [.., PdfName xobjectName] && ResolveResource(resources, PdfNames.XObject, xobjectName) is PdfStream xobject:
                            ScanXObject(xobject, resources, cache, depth);
                            break;
                        case "BI" when operands is [PdfDictionary image, ..]:
                            if (!image.GetBoolean(IMName) && !image.GetBoolean(ImageMaskName) && (image[CsName] ?? image[PdfNames.ColorSpace]) is { } inlineColorSpace)
                            {
                                var expanded = inlineColorSpace is PdfName { Value: "G" or "RGB" or "CMYK" or "I" } abbreviation
                                    ? PdfName.Create(abbreviation.Value switch { "G" => "DeviceGray", "RGB" => "DeviceRGB", "CMYK" => "DeviceCMYK", _ => "Indexed" })
                                    : inlineColorSpace is PdfName inlineName ? ResolveResource(resources, PdfNames.ColorSpace, inlineName) ?? inlineName : inlineColorSpace;
                                AddColorSpace(expanded, resources, cache);
                            }

                            break;
                    }
                }
                catch (PdfException)
                {
                    // A damaged object: the next operators are still read
                }
            }
        }

        /// <summary>Records a device color: the process colorants of its nonzero components (gray is black, RGB is separated into the process colorants).</summary>
        private void AddDeviceColor(string family, List<PdfObject> operands)
        {
            _families.Add(family);
            Span<double> values = stackalloc double[4];
            var count = Math.Min(operands.Count, 4);
            for (var i = 0; i < count; i++)
            {
                values[i] = operands[i] is PdfNumber number && double.IsFinite(number.DoubleValue) ? number.DoubleValue : 0;
            }

            switch (family)
            {
                case "DeviceGray" when count >= 1 && values[0] < 1:
                    _process.Add("Black");
                    break;
                case "DeviceRGB" when count >= 3:
                    foreach (var colorant in RenderColorSpace.ProcessColorants)
                    {
                        if (RenderColorSpace.TryGetProcessTint(new RgbColor(values[0], values[1], values[2]), colorant, out var tint) && tint > 0)
                        {
                            _process.Add(colorant);
                        }
                    }

                    break;
                case "DeviceCMYK" when count >= 4:
                    for (var i = 0; i < 4; i++)
                    {
                        if (values[i] > 0)
                        {
                            _process.Add(RenderColorSpace.ProcessColorants[i]);
                        }
                    }

                    break;
            }
        }

        private void ScanXObject(PdfStream xobject, PdfDictionary? resources, RenderCache cache, int depth)
        {
            var dictionary = xobject.Dictionary;
            switch (dictionary.GetName(PdfNames.Subtype)?.Value)
            {
                case "Image":
                    if (!dictionary.GetBoolean(ImageMaskName) && dictionary.GetResolved(PdfNames.ColorSpace) is { } colorSpace)
                    {
                        AddColorSpace(colorSpace, resources, cache);
                    }

                    break;
                case "Form":
                    ScanForm(xobject, resources, cache, depth + 1);
                    break;
            }
        }

        private void ScanPattern(PdfObject pattern, PdfDictionary? resources, RenderCache cache, int depth)
        {
            var dictionary = pattern is PdfStream stream ? stream.Dictionary : pattern as PdfDictionary;
            if (dictionary?.GetInteger(PatternTypeName) == 2 && dictionary.GetResolved(PdfNames.Shading) is { } shading)
            {
                ScanShading(shading, resources, cache);
            }
            else if (pattern is PdfStream cell)
            {
                ScanForm(cell, resources, cache, depth + 1);
            }
        }

        private void ScanShading(PdfObject shading, PdfDictionary? resources, RenderCache cache)
        {
            var dictionary = shading is PdfStream stream ? stream.Dictionary : shading as PdfDictionary;
            if (dictionary?.GetResolved(PdfNames.ColorSpace) is { } colorSpace)
            {
                AddColorSpace(colorSpace, resources, cache);
            }
        }

        /// <summary>Records a color space and the colorants it paints.</summary>
        private void AddColorSpace(PdfObject colorSpace, PdfDictionary? resources, RenderCache cache, int depth = 0)
        {
            if (depth > MaxDepth)
                return;

            colorSpace = PdfReference.ResolveIfReference(colorSpace);
            var family = colorSpace switch
            {
                PdfName name => name.Value,
                PdfArray { Count: > 0 } array => (array.GetResolved(0) as PdfName)?.Value,
                _ => null,
            };
            if (family is null)
                return;

            family = family switch { "G" => "DeviceGray", "RGB" => "DeviceRGB", "CMYK" => "DeviceCMYK", "I" => "Indexed", _ => family };
            _families.Add(family);
            var array2 = colorSpace as PdfArray;
            switch (family)
            {
                case "DeviceGray" or "CalGray":
                    _process.Add("Black");
                    break;
                case "DeviceCMYK" or "DeviceRGB" or "CalRGB" or "Lab":
                    _process.UnionWith(RenderColorSpace.ProcessColorants);
                    break;
                case "ICCBased" when array2 is { Count: >= 2 } && array2.GetResolved(1) is PdfStream profile:
                    if (profile.Dictionary.GetInteger(PdfNames.N) == 1)
                    {
                        _process.Add("Black");
                    }
                    else
                    {
                        _process.UnionWith(RenderColorSpace.ProcessColorants);
                    }

                    break;
                case "Indexed" when array2 is { Count: >= 2 }:
                    AddColorSpace(array2.GetResolved(1) ?? PdfNull.Instance, resources, cache, depth + 1);
                    break;
                case "Pattern" when array2 is { Count: >= 2 }:
                    AddColorSpace(array2.GetResolved(1) ?? PdfNull.Instance, resources, cache, depth + 1);
                    break;
                case "Separation" when array2 is { Count: >= 4 } && array2.GetResolved(1) is PdfName colorant:
                    AddColorants(array2, [colorant.Value], resources, cache);
                    break;
                case "DeviceN" when array2 is { Count: >= 4 } && array2.GetResolved(1) is PdfArray names:
                    AddColorants(array2, [.. names.Select(item => PdfReference.ResolveIfReference(item) is PdfName n ? n.Value : "")], resources, cache);
                    break;
            }
        }

        private void AddColorants(PdfArray colorSpace, string[] colorants, PdfDictionary? resources, RenderCache cache)
        {
            RenderColorSpace? space = null;
            for (var i = 0; i < colorants.Length; i++)
            {
                var colorant = colorants[i];
                if (colorant is "None" or "")
                    continue;

                if (colorant == "All")
                {
                    _process.UnionWith(RenderColorSpace.ProcessColorants);
                    continue;
                }

                if (Array.IndexOf(RenderColorSpace.ProcessColorants, colorant) >= 0)
                {
                    _process.Add(colorant);
                    continue;
                }

                if (SpotColors.ContainsKey(colorant))
                    continue;

                // The color of the full tint of the colorant alone, in the alternate color space
                RgbColor color;
                try
                {
                    space ??= RenderColorSpace.Read(colorSpace, resources, cache);
                    var components = new double[colorants.Length];
                    components[i] = 1;
                    color = space.ToRgb(components);
                }
                catch (PdfException)
                {
                    color = new RgbColor(0, 0, 0);
                }

                SpotColors[colorant] = color;
            }
        }

        private static PdfObject? ResolveResource(PdfDictionary? resources, PdfName category, PdfName name)
        {
            return resources?.GetResolved(category) is PdfDictionary dictionary ? dictionary.GetResolved(name) : null;
        }
    }
}
