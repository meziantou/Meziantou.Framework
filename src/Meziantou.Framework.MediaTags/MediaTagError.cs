namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Specifies the type of error that occurred during a media tag operation.
/// </summary>
public enum MediaTagError
{
    /// <summary>The file format is not supported or could not be detected.</summary>
    UnsupportedFormat,

    /// <summary>The file is corrupt or contains invalid data.</summary>
    CorruptFile,

    /// <summary>The stream ended before expected data could be read.</summary>
    UnexpectedEndOfStream,

    /// <summary>Tag data is malformed or cannot be parsed.</summary>
    InvalidTagData,

    /// <summary>Text encoding error in tag data.</summary>
    EncodingError,

    /// <summary>An I/O error occurred while reading or writing the file.</summary>
    IoError,
}
