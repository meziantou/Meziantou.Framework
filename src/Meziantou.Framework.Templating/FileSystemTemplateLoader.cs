namespace Meziantou.Framework.Templating;

/// <summary>Loads templates from files.</summary>
/// <remarks>
/// A name is a path relative to the directory of the template that contains the directive. When that template has no
/// file name, the name is relative to <see cref="RootDirectory"/>. The full name of a template is its absolute path.
/// </remarks>
public sealed class FileSystemTemplateLoader : TemplateLoader
{
    /// <summary>Initializes a new instance of the <see cref="FileSystemTemplateLoader"/> class.</summary>
    /// <param name="rootDirectory">The directory that relative names and relative <see cref="Template.SourceFileName"/> values are resolved from.</param>
    public FileSystemTemplateLoader(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Gets the absolute path of the directory that relative names are resolved from.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public override string GetFullName(string name, string? referrerFullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var baseDirectory = RootDirectory;
        if (!string.IsNullOrEmpty(referrerFullName))
        {
            baseDirectory = Path.GetDirectoryName(Path.GetFullPath(referrerFullName, RootDirectory)) ?? RootDirectory;
        }

        return Path.GetFullPath(name, baseDirectory);
    }

    /// <inheritdoc/>
    public override string? GetText(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return File.Exists(fullName) ? File.ReadAllText(fullName) : null;
    }
}
