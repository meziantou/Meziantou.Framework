namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>A script of a form failed, and <see cref="PdfFormScriptOptions.ThrowOnScriptError"/> is set.</summary>
public sealed class PdfFormScriptException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfFormScriptException"/> class.</summary>
    public PdfFormScriptException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfFormScriptException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PdfFormScriptException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfFormScriptException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The error of the script.</param>
    public PdfFormScriptException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
