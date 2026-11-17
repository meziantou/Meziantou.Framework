namespace Meziantou.Framework.Compression;

/// <summary>The exception that is thrown when an encrypted archive or entry is read without a password, or with a wrong password.</summary>
public sealed class ArchivePasswordException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="ArchivePasswordException"/> class.</summary>
    public ArchivePasswordException()
        : base("The archive is encrypted and the password is missing or wrong.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArchivePasswordException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public ArchivePasswordException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArchivePasswordException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ArchivePasswordException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
