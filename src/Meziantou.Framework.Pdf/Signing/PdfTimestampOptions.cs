using System.Security.Cryptography;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>The options of a document time stamp (ISO 32000-2, 12.8.5): the RFC 3161 time stamp authority and the field of the time stamp.</summary>
public sealed class PdfTimestampOptions
{
    /// <summary>Initializes a new instance of the <see cref="PdfTimestampOptions"/> class.</summary>
    /// <param name="timestampAuthority">The URL of the RFC 3161 time stamp authority.</param>
    public PdfTimestampOptions(Uri timestampAuthority)
    {
        ArgumentNullException.ThrowIfNull(timestampAuthority);
        TimestampAuthority = timestampAuthority;
    }

    /// <summary>Gets the URL of the RFC 3161 time stamp authority.</summary>
    public Uri TimestampAuthority { get; }

    /// <summary>Gets the client the time stamp request is sent with; <see langword="null"/> for a client of the library.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Gets the digest algorithm of the time stamp. The default is SHA-256.</summary>
    public HashAlgorithmName HashAlgorithm { get; init; } = HashAlgorithmName.SHA256;

    /// <summary>Gets the name of the field of the time stamp. The default is <c>Signature</c> followed by the first number not used.</summary>
    public string? FieldName { get; init; }

    /// <summary>Gets the bytes reserved for the time stamp token in the file; 0 (the default) for 16 KiB.</summary>
    public int ReservedSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }
}
