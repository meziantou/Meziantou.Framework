namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// An error of a cryptographic operation, thrown as the <c>DOMException</c> of the same name by the Web Cryptography API
/// (https://w3c.github.io/webcrypto/#SubtleCrypto-Exceptions): e.g. <c>DataError</c> for invalid key data.
/// </summary>
#pragma warning disable CA1064 // The exception never leaves the library: the bindings convert it to a DOMException
internal sealed class WebCryptoException : Exception
#pragma warning restore CA1064
{
    public WebCryptoException()
        : this("OperationError", "The operation failed")
    {
    }

    public WebCryptoException(string message)
        : this("OperationError", message)
    {
    }

    public WebCryptoException(string message, Exception innerException)
        : base(message, innerException)
    {
        Name = "OperationError";
    }

    public WebCryptoException(string name, string message)
        : base(message)
    {
        Name = name;
    }

    /// <summary>Gets the name of the DOMException.</summary>
    public string Name { get; }

    public static WebCryptoException DataError(string message) => new("DataError", message);

    public static WebCryptoException OperationError(string message) => new("OperationError", message);
}
