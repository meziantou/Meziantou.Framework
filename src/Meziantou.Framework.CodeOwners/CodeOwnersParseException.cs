namespace Meziantou.Framework.CodeOwners;

/// <summary>The exception thrown by <see cref="CodeOwnersParser.Parse(string)"/> when a CODEOWNERS file is invalid.</summary>
public sealed class CodeOwnersParseException : Exception
{
    /// <summary>Initializes a new instance of <see cref="CodeOwnersParseException"/>.</summary>
    public CodeOwnersParseException()
        : base("The CODEOWNERS file is invalid.")
    {
    }

    /// <summary>Initializes a new instance of <see cref="CodeOwnersParseException"/> with the specified message.</summary>
    public CodeOwnersParseException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="CodeOwnersParseException"/> with the specified message and inner exception.</summary>
    public CodeOwnersParseException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal CodeOwnersParseException(CodeOwnersError error)
        : base($"The CODEOWNERS file is invalid at {error}")
    {
        Error = error;
    }

    /// <summary>Gets the error that made the file invalid. Its <see cref="CodeOwnersError.LineNumber"/> is 0 when the exception was not created by the parser.</summary>
    public CodeOwnersError Error { get; }
}
