using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Represents an embedded picture (album art) in a media file.
/// </summary>
public sealed class MediaPicture
{
    /// <summary>Gets or sets the type of the picture.</summary>
    public MediaPictureType PictureType { get; set; }

    /// <summary>Gets or sets the MIME type of the picture data (e.g. "image/jpeg", "image/png").</summary>
    public string? MimeType { get; set; }

    /// <summary>Gets or sets an optional description of the picture.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the raw picture data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Data { get; set; } = [];
}
