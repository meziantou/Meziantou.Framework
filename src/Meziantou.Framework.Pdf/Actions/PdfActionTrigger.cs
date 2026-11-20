namespace Meziantou.Framework.Pdf;

/// <summary>The events of the additional actions (<c>/AA</c>, ISO 32000-2, 12.6.3, tables 197 to 200).</summary>
public enum PdfActionTrigger
{
    /// <summary>The mouse enters the annotation (<c>E</c>).</summary>
    MouseEnter,

    /// <summary>The mouse leaves the annotation (<c>X</c>).</summary>
    MouseExit,

    /// <summary>The mouse button is pressed on the annotation (<c>D</c>).</summary>
    MouseDown,

    /// <summary>The mouse button is released on the annotation (<c>U</c>).</summary>
    MouseUp,

    /// <summary>The annotation gets the focus (<c>Fo</c>).</summary>
    Focus,

    /// <summary>The annotation loses the focus (<c>Bl</c>).</summary>
    Blur,

    /// <summary>The page of the annotation is opened (<c>PO</c>).</summary>
    AnnotationPageOpen,

    /// <summary>The page of the annotation is closed (<c>PC</c>).</summary>
    AnnotationPageClose,

    /// <summary>The page of the annotation becomes visible (<c>PV</c>).</summary>
    AnnotationPageVisible,

    /// <summary>The page of the annotation is no longer visible (<c>PI</c>).</summary>
    AnnotationPageInvisible,

    /// <summary>The page is opened (<c>O</c> of a page).</summary>
    PageOpen,

    /// <summary>The page is closed (<c>C</c> of a page).</summary>
    PageClose,

    /// <summary>The value of a form field is typed or selected (<c>K</c>).</summary>
    Keystroke,

    /// <summary>The value of a form field is formatted to be shown (<c>F</c>).</summary>
    Format,

    /// <summary>The value of a form field is validated (<c>V</c>).</summary>
    Validate,

    /// <summary>The value of a form field is recalculated (<c>C</c> of a field).</summary>
    Calculate,

    /// <summary>The document is about to be closed (<c>WC</c>).</summary>
    DocumentClose,

    /// <summary>The document is about to be saved (<c>WS</c>).</summary>
    DocumentSave,

    /// <summary>The document was saved (<c>DS</c>).</summary>
    DocumentSaved,

    /// <summary>The document is about to be printed (<c>WP</c>).</summary>
    DocumentPrint,

    /// <summary>The document was printed (<c>DP</c>).</summary>
    DocumentPrinted,
}
