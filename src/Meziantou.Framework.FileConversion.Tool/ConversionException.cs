namespace Meziantou.Framework.FileConversionTool;

/// <summary>An error of the conversion, reported to the user without a stack trace.</summary>
internal sealed class ConversionException : Exception
{
    public ConversionException()
    {
    }

    public ConversionException(string message)
        : base(message)
    {
    }

    public ConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
