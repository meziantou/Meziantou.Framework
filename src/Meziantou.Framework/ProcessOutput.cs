namespace Meziantou.Framework;

/// <summary>
/// Represents a single line of output from a process.
/// </summary>
public sealed class ProcessOutput
{
    internal ProcessOutput(ProcessOutputType type, string text)
    {
        Type = type;
        Text = text;
    }

    /// <summary>
    /// Gets the type of output stream this output came from.
    /// </summary>
    public ProcessOutputType Type { get; }

    /// <summary>
    /// Gets the text of the output.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Deconstructs the process output into its type and text.
    /// </summary>
    /// <param name="type">The output type.</param>
    /// <param name="text">The output text.</param>
    public void Deconstruct(out ProcessOutputType type, out string text)
    {
        type = Type;
        text = Text;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Type switch
        {
            ProcessOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}
