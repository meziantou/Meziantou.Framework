using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Optimization;

/// <summary>The names of the resources a content stream uses (ISO 32000-2, 7.8.3), by category.</summary>
internal static class ContentResourceUsage
{
    private const int MaxFormDepth = 16;

    private static readonly PdfName FontName = PdfNames.Font;
    private static readonly PdfName PatternName = PdfNames.Pattern;
    private static readonly PdfName ShadingName = PdfNames.Shading;
    private static readonly PdfName PropertiesName = PdfNames.Properties;
    private static readonly PdfName CsName = PdfName.Create("CS");
    private static readonly PdfName DefaultGrayName = PdfName.Create("DefaultGray");
    private static readonly PdfName DefaultRgbName = PdfName.Create("DefaultRGB");
    private static readonly PdfName DefaultCmykName = PdfName.Create("DefaultCMYK");

    /// <summary>
    /// Collects the names a content uses. The forms painted that have no resources of their own use the resources of
    /// the content (a practice of PDF 1.1, 7.8.3): their content is read too.
    /// </summary>
    /// <returns><see langword="false"/> when the usage cannot be known: the content cannot be read, or uses a Type 3 font without resources of its own.</returns>
    public static bool TryCollect(ReadOnlyMemory<byte> content, PdfDictionary? resources, HashSet<(PdfName Category, PdfName Name)> used)
    {
        return TryCollect(content, resources, used, depth: 0);
    }

    private static bool TryCollect(ReadOnlyMemory<byte> content, PdfDictionary? resources, HashSet<(PdfName Category, PdfName Name)> used, int depth)
    {
        // The default color spaces (8.6.5.6) replace the device color spaces without being named by the content:
        // the color operators (rg, k, g...), the images and the shadings use them
        used.Add((PdfNames.ColorSpace, DefaultGrayName));
        used.Add((PdfNames.ColorSpace, DefaultRgbName));
        used.Add((PdfNames.ColorSpace, DefaultCmykName));

        var reader = new ContentStreamReader(content);
        var operands = new List<PdfObject>();
        try
        {
            while (reader.TryRead(operands, out var name))
            {
                switch (name)
                {
                    case "Tf" when operands is [PdfName font, ..]:
                        used.Add((FontName, font));
                        if (IsType3WithoutResources(GetResource(resources, FontName, font)))
                            return false;

                        break;

                    case "Do" when operands is [.., PdfName xobject]:
                        used.Add((PdfNames.XObject, xobject));
                        if (GetResource(resources, PdfNames.XObject, xobject) is PdfStream form && form.Dictionary.GetName(PdfNames.Subtype)?.Value == "Form" && !form.Dictionary.ContainsKey(PdfNames.Resources))
                        {
                            if (depth >= MaxFormDepth || !TryCollect(form.GetDecodedData(), resources, used, depth + 1))
                                return false;
                        }

                        break;

                    case "gs" when operands is [PdfName state, ..]:
                        used.Add((PdfNames.ExtGState, state));

                        // A graphics state can select a font (8.4.5): a Type 3 font without resources uses those of the content
                        if (GetResource(resources, PdfNames.ExtGState, state) is PdfDictionary stateDictionary && IsType3WithoutResources(Resolve(stateDictionary, PdfNames.Font) is PdfArray { Count: > 0 } fontEntry ? Resolve(fontEntry[0]) : null))
                            return false;

                        break;

                    case "cs" or "CS" when operands is [PdfName colorSpace, ..]:
                        used.Add((PdfNames.ColorSpace, colorSpace));
                        break;

                    case "scn" or "SCN" when operands is [.., PdfName pattern]:
                        used.Add((PatternName, pattern));
                        break;

                    case "sh" when operands is [PdfName shading, ..]:
                        used.Add((ShadingName, shading));
                        break;

                    case "BDC" or "DP" when operands is [_, PdfName properties, ..]:
                        used.Add((PropertiesName, properties));
                        break;

                    case "BI" when operands is [PdfDictionary image, ..]:
                        if ((image.Get<PdfName>(CsName) ?? image.Get<PdfName>(PdfNames.ColorSpace)) is { } inlineColorSpace)
                        {
                            used.Add((PdfNames.ColorSpace, inlineColorSpace));
                        }

                        break;
                }
            }
        }
        catch (PdfException)
        {
            return false;
        }

        return true;
    }

    private static bool IsType3WithoutResources(PdfObject? font) => font is PdfDictionary dictionary && dictionary.GetName(PdfNames.Subtype)?.Value == "Type3" && !dictionary.ContainsKey(PdfNames.Resources);

    private static PdfObject? Resolve(PdfDictionary dictionary, PdfName key)
    {
        try
        {
            return dictionary.GetResolved(key);
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static PdfObject? Resolve(PdfObject value)
    {
        try
        {
            return value is PdfReference reference ? reference.Resolve() : value;
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static PdfObject? GetResource(PdfDictionary? resources, PdfName category, PdfName name)
    {
        try
        {
            return resources?.GetResolved(category) is PdfDictionary dictionary ? dictionary.GetResolved(name) : null;
        }
        catch (PdfException)
        {
            return null;
        }
    }
}
