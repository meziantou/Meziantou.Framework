namespace Meziantou.Framework;

/// <summary>Represents a single line of process output with its stream type and UTC timestamp.</summary>
public sealed class ProcessOutput
{
    internal ProcessOutput(ProcessOutputType type, string text)
    {
        Type = type;
        Text = text;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Gets the type of output stream this line came from.</summary>
    public ProcessOutputType Type { get; }

    /// <summary>Gets the text content of this output line.</summary>
    public string Text { get; }

    /// <summary>Gets the UTC timestamp at which this output line was captured.</summary>
    public DateTime TimestampUtc { get; }

    /// <summary>Deconstructs the output into its type and text components.</summary>
    public void Deconstruct(out ProcessOutputType type, out string text)
    {
        type = Type;
        text = Text;
    }

    /// <summary>Deconstructs the output into its type, text, and UTC timestamp components.</summary>
    public void Deconstruct(out ProcessOutputType type, out string text, out DateTime timestampUtc)
    {
        type = Type;
        text = Text;
        timestampUtc = TimestampUtc;
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
