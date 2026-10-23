namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfDocument.Open(byte[], PdfReadOptions?)"/>. The limits bound the memory and the work of hostile files.</summary>
public sealed class PdfReadOptions
{
    internal const long DefaultMaxDecodedStreamLength = 256L * 1024 * 1024;
    internal const int DefaultMaxNestingDepth = 256;

    /// <summary>Gets the default options.</summary>
    public static PdfReadOptions Default { get; } = new();

    /// <summary>Gets the maximum length of the decoded data of one stream, in bytes. The default is 256 MiB.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxDecodedStreamLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxDecodedStreamLength;

    /// <summary>Gets the maximum depth of nested arrays and dictionaries. The default is 256.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxNestingDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxNestingDepth;

    /// <summary>Gets the password of an encrypted document: the user or the owner password. The default is the empty password.</summary>
    public string? Password { get; init; }
}
