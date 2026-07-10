namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An image loaded from a tar archive produced by <c>docker save</c>.</summary>
/// <param name="ArchivePath">The path to the image archive.</param>
internal sealed record ArchiveImage(string ArchivePath) : ImageSource;
