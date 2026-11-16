namespace Meziantou.Framework.FileConversionTool.Epub;

/// <summary>A resource of the manifest of a book.</summary>
/// <param name="Id">The identifier of the item.</param>
/// <param name="Path">The path of the resource in the archive.</param>
/// <param name="MediaType">The media type of the resource.</param>
/// <param name="Properties">The properties of the item (<c>nav</c>, <c>cover-image</c>, <c>svg</c>...).</param>
internal sealed record EpubManifestItem(string Id, string Path, string MediaType, string Properties)
{
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
