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

    internal CodeOwnersParseException(CodeOwnersErrorKind kind, int lineNumber, int linePosition)
        : base($"The CODEOWNERS file is invalid at line {lineNumber}, position {linePosition}: {GetDescription(kind)}")
    {
        Kind = kind;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>Gets the kind of error that made the file invalid.</summary>
    public CodeOwnersErrorKind Kind { get; }

    /// <summary>Gets the one-based number of the line containing the error, or 0 when the position is unknown.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the one-based position of the error within its line, or 0 when the position is unknown.</summary>
    public int LinePosition { get; }

    private static string GetDescription(CodeOwnersErrorKind kind) => kind switch
    {
        CodeOwnersErrorKind.UnterminatedSectionHeader => "the section header is not terminated by ']'",
        CodeOwnersErrorKind.UnterminatedRequiredReviewerCount => "the required reviewer count is not terminated by ']'",
        CodeOwnersErrorKind.InvalidRequiredReviewerCount => "the required reviewer count is not a positive integer",
        CodeOwnersErrorKind.EmptyMember => "'@' does not identify an owner",
        CodeOwnersErrorKind.InvalidMember => "the owner is neither a username nor an email address",
        _ => kind.ToString(),
    };
}
