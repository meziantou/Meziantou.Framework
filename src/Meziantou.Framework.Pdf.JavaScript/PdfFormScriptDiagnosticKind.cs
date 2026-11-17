namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>The kinds of <see cref="PdfFormScriptDiagnostic"/>.</summary>
public enum PdfFormScriptDiagnosticKind
{
    /// <summary>A script threw an error, or cannot be parsed.</summary>
    Error,

    /// <summary>A script exceeded a limit of the options (time, statements, memory, calls) or was canceled.</summary>
    LimitExceeded,

    /// <summary>A script called a function that reaches outside of the form (submit, mail, print, open a URL...): the call did nothing.</summary>
    BlockedCall,

    /// <summary>A script called <c>app.alert</c>.</summary>
    Alert,
}
