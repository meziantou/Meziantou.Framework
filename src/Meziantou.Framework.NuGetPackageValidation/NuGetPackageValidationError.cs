namespace Meziantou.Framework.NuGetPackageValidation;

public sealed class NuGetPackageValidationError
{
    public NuGetPackageValidationError(int errorCode, string message, string? helpText, string? fileName = null)
    {
        ErrorCode = errorCode;
        Message = message;
        HelpText = helpText;
        FileName = fileName;
    }

    public int ErrorCode { get; }
    public string Message { get; }
    public string? HelpText { get; }
    public string? FileName { get; }

    public override string ToString()
    {
        if (FileName is null)
            return $"{ErrorCode}: {Message}";

        return $"{ErrorCode} - {FileName}: {Message}";
    }
}
