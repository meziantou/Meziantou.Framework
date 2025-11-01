namespace Meziantou.Framework.Templating;

/// <summary>Provides output writing functionality for templates.</summary>
public class Output
{
    /// <summary>Gets the template that this output is associated with.</summary>
    public Template Template { get; }

    /// <summary>Gets the text writer used to write output.</summary>
    public TextWriter Writer { get; }

    /// <summary>Initializes a new instance of the <see cref="Output"/> class.</summary>
    /// <param name="template">The template that this output is associated with.</param>
    /// <param name="writer">The text writer to write output to.</param>
    public Output(Template template, TextWriter writer)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <summary>Writes the text representation of an object to the output.</summary>
    /// <param name="value">The object to write.</param>
    public virtual void Write(object? value)
    {
        Write("{0}", value);
    }

    /// <summary>Writes a string to the output.</summary>
    /// <param name="value">The string to write.</param>
    public virtual void Write(string? value)
    {
        Write("{0}", value);
    }

    /// <summary>Writes a formatted string to the output.</summary>
    /// <param name="format">The format string.</param>
    /// <param name="args">The objects to format.</param>
    public virtual void Write(string format, params object?[] args)
    {
        Writer.Write(format, args);
    }
}
