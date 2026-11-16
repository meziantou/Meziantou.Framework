namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>The scripts a session runs (ISO 32000-2, 12.6.3, table 199).</summary>
public enum PdfFormScriptEvent
{
    /// <summary>A document-level script, run when the session is created.</summary>
    Document,

    /// <summary>The keystroke script of a field (<c>/K</c>), run when the value is committed.</summary>
    Keystroke,

    /// <summary>The validation script of a field (<c>/V</c>).</summary>
    Validate,

    /// <summary>The calculation script of a field (<c>/C</c>).</summary>
    Calculate,

    /// <summary>The format script of a field (<c>/F</c>), which gives the text shown for the value.</summary>
    Format,
}
