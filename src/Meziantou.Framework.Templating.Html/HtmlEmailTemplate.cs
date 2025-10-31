namespace Meziantou.Framework.Templating;

/// <summary>Provides a templating engine specialized for generating HTML email content with automatic encoding and metadata extraction.</summary>
/// <example>
/// <code>
/// var template = new HtmlEmailTemplate();
/// template.Load("{{@begin section title}}Welcome{{@end section}}&lt;h1&gt;Hello {{#html userName}}!&lt;/h1&gt;");
/// var result = template.Run(out var metadata, new Dictionary&lt;string, object?&gt; { ["userName"] = "John" });
/// // result: &lt;h1&gt;Hello John!&lt;/h1&gt;
/// // metadata.Title: "Welcome"
/// </code>
/// </example>
public class HtmlEmailTemplate : Template
{
    public HtmlEmailTemplate()
    {
        StartCodeBlockDelimiter = "{{";
        EndCodeBlockDelimiter = "}}";
    }

    /// <summary>Creates an HTML-specific code block that supports HTML encoding, URL encoding, sections, and content identifiers.</summary>
    protected override CodeBlock CreateCodeBlock(string text, int index)
    {
        return new HtmlEmailCodeBlock(this, text, index);
    }

    /// <summary>Creates an HTML email output writer with encoding capabilities and section support.</summary>
    protected override object CreateOutput(TextWriter writer)
    {
        return new HtmlEmailOutput(this, writer);
    }

    /// <summary>Runs the template with named parameters and returns the generated HTML along with extracted metadata.</summary>
    /// <param name="metadata">Receives metadata extracted from the template, including title and content identifiers.</param>
    /// <param name="parameters">Named parameters to pass to the template.</param>
    /// <returns>The generated HTML content.</returns>
    public virtual string Run(out HtmlEmailMetadata? metadata, IDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        using var writer = new StringWriter();
        Run(writer, out metadata, parameters);
        return writer.ToString();
    }

    /// <summary>Runs the template with positional parameters and returns the generated HTML along with extracted metadata.</summary>
    /// <param name="metadata">Receives metadata extracted from the template, including title and content identifiers.</param>
    /// <param name="parameters">Positional parameters to pass to the template.</param>
    /// <returns>The generated HTML content.</returns>
    public virtual string Run(out HtmlEmailMetadata? metadata, params object?[] parameters)
    {
        using var writer = new StringWriter();
        Run(writer, out metadata, parameters);
        return writer.ToString();
    }

    /// <summary>Runs the template without parameters and returns the generated HTML along with extracted metadata.</summary>
    /// <param name="metadata">Receives metadata extracted from the template, including title and content identifiers.</param>
    /// <returns>The generated HTML content.</returns>
    public virtual string Run(out HtmlEmailMetadata? metadata)
    {
        using var writer = new StringWriter();
        Run(writer, out metadata);
        return writer.ToString();
    }

    /// <summary>Runs the template with named parameters and writes the generated HTML to the specified writer.</summary>
    /// <param name="writer">The text writer to write the generated HTML to.</param>
    /// <param name="metadata">Receives metadata extracted from the template, including title and content identifiers.</param>
    /// <param name="parameters">Named parameters to pass to the template.</param>
    public virtual void Run(TextWriter writer, out HtmlEmailMetadata? metadata, IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(parameters);

        var p = CreateMethodParameters(writer, parameters);
        InvokeRunMethod(p);
        metadata = GetMetadata(p);
    }

    /// <summary>Runs the template with positional parameters and writes the generated HTML to the specified writer.</summary>
    /// <param name="writer">The text writer to write the generated HTML to.</param>
    /// <param name="metadata">Receives metadata extracted from the template, including title and content identifiers.</param>
    /// <param name="parameters">Positional parameters to pass to the template.</param>
    public virtual void Run(TextWriter writer, out HtmlEmailMetadata? metadata, params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var p = CreateMethodParameters(writer, parameters);
        InvokeRunMethod(p);
        metadata = GetMetadata(p);
    }

    /// <summary>Runs the template without parameters and writes the generated HTML to the specified writer.</summary>
    /// <param name="writer">The text writer to write the generated HTML to.</param>
    /// <param name="metadata">Receives metadata extracted from the template, including title and content identifiers.</param>
    public virtual void Run(TextWriter writer, out HtmlEmailMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var p = CreateMethodParameters(writer, (object[]?)null);
        InvokeRunMethod(p);
        metadata = GetMetadata(p);
    }

    private static HtmlEmailMetadata? GetMetadata(object?[] parameters)
    {
        var htmlEmailOutput = parameters.OfType<HtmlEmailOutput>().FirstOrDefault();
        if (htmlEmailOutput is not null)
        {
            return new HtmlEmailMetadata
            {
                Title = htmlEmailOutput.GetSection(HtmlEmailOutput.TitleSectionName),
                ContentIdentifiers = htmlEmailOutput.ContentIdentifiers,
            };
        }

        return null;
    }
}
