namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>The states of the syntax extensions for one document, created the first time an extension asks for its state.</summary>
internal sealed class SyntaxExtensionStates(SyntaxExtensionSet extensions)
{
    private readonly object?[] _states = new object?[extensions.Extensions.Length];
    private readonly bool[] _created = new bool[extensions.Extensions.Length];

    public object? Get(int index)
    {
        if (!_created[index])
        {
            _created[index] = true;
            _states[index] = extensions.Extensions[index].CreateState();
        }

        return _states[index];
    }

    // The state of another extension of the options, such as the block extension whose definitions an inline extension reads
    public object? Get(MarkdownSyntaxExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (!extensions.TryGetIndex(extension, out var index))
        {
            throw new ArgumentException($"The syntax extension '{extension.Name}' is not in the options.", nameof(extension));
        }

        return Get(index);
    }
}
