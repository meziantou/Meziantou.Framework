namespace Meziantou.Framework.FileConversionTool.Epub;

/// <summary>A resource of the manifest of a book.</summary>
/// <param name="Id">The identifier of the item.</param>
/// <param name="Path">The path of the resource in the archive.</param>
/// <param name="MediaType">The media type of the resource.</param>
/// <param name="Properties">The properties of the item (<c>nav</c>, <c>cover-image</c>, <c>svg</c>...).</param>
internal sealed record EpubManifestItem(string Id, string Path, string MediaType, string Properties)
{
    /// <summary>Gets a value indicating whether the resource is a raster image, which is a page as it is. An SVG image is a document, which is laid out.</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !IsSvg(MediaType, Path);

    /// <summary>Gets a value indicating whether a resource is an SVG image, from its media type or its extension.</summary>
    public static bool IsSvg(string? mediaType, string path)
        => string.Equals(mediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
}
