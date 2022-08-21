namespace Meziantou.Framework.DependencyScanning.Locations;

internal sealed class NonUpdatableLocation : Location
{
    public NonUpdatableLocation(ScanFileContext context)
        : base(context.FileSystem, context.FullPath)
    {
    }

    public NonUpdatableLocation(IFileSystem fileSystem, string filePath)
        : base(fileSystem, filePath)
    {
    }

    public override bool IsUpdatable => false;

    protected internal override Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Cannot update this location");
    }
}
