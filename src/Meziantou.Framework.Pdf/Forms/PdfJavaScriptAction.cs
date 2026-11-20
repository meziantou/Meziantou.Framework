using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A JavaScript action (ISO 32000-2, 12.6.4.17): a script a viewer runs.</summary>
public sealed class PdfJavaScriptAction : PdfAction
{
    internal static readonly PdfName JSName = PdfName.Create("JS");

    internal PdfJavaScriptAction(PdfDictionary dictionary, string script)
        : base(dictionary, document: null)
    {
        Script = script;
    }

    /// <summary>Gets the script: the text string or the stream of <c>/JS</c>.</summary>
    public string Script { get; }

    /// <summary>Reads a JavaScript action: a dictionary whose <c>/S</c> is <c>/JavaScript</c>; <see langword="null"/> for another value.</summary>
    internal static PdfJavaScriptAction? Read(PdfObject? value)
    {
        if (value is not PdfDictionary dictionary || dictionary.GetName(PdfNames.S)?.Value != "JavaScript")
            return null;

        var script = ReadScript(dictionary.GetResolved(JSName));
        return script is null ? null : new PdfJavaScriptAction(dictionary, script);
    }

    /// <summary>Reads a script: a text string, or a stream of text (PDFDocEncoding, or UTF-16BE with a byte order mark, 7.9.2.2).</summary>
    internal static string? ReadScript(PdfObject? value)
    {
        try
        {
            return value switch
            {
                PdfString text => text.ToText(),
                PdfStream stream => PdfString.FromBytes(stream.GetDecodedData()).ToText(),
                _ => null,
            };
        }
        catch (PdfException)
        {
            return null;
        }
    }
}
