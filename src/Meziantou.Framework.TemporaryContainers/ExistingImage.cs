namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An image that already exists locally, referenced by id or name.</summary>
/// <param name="ImageId">The image id or name.</param>
public sealed record ExistingImage(string ImageId) : ImageSource;
