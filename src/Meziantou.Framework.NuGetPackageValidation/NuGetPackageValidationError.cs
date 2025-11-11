namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Represents a validation error found during NuGet package validation.</summary>
public sealed class NuGetPackageValidationError
{
    /// <summary>Initializes a new instance of the <see cref="NuGetPackageValidationError"/> class.</summary>
    /// <param name="errorCode">The numeric error code identifying the type of validation error.</param>
    /// <param name="message">A human-readable message describing the validation error.</param>
    /// <param name="helpText">Optional help text providing guidance on how to fix the error.</param>
    /// <param name="fileName">Optional file name within the package where the error was found.</param>
    public NuGetPackageValidationError(int errorCode, string message, string? helpText, string? fileName = null)
    {
        ErrorCode = errorCode;
        Message = message;
        HelpText = helpText;
        FileName = fileName;
    }

    /// <summary>Gets the numeric error code identifying the type of validation error.</summary>
    public int ErrorCode { get; }

    /// <summary>Gets a human-readable message describing the validation error.</summary>
    public string Message { get; }

    /// <summary>Gets optional help text providing guidance on how to fix the error.</summary>
    public string? HelpText { get; }

    /// <summary>Gets the optional file name within the package where the error was found.</summary>
    public string? FileName { get; }

    public override string ToString()
    {
        if (FileName is null)
            return $"{ErrorCode}: {Message}";

        return $"{ErrorCode} - {FileName}: {Message}";
    }
}
