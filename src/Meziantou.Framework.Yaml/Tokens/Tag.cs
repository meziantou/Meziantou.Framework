namespace Meziantou.Framework.Yaml.Tokens;

/// <summary>Represents a tag token.</summary>
public class Tag : Token
{

    /// <summary>Gets the handle.</summary>
    /// <value>The handle.</value>
    public string Handle { get; }

    /// <summary>Gets the suffix.</summary>
    /// <value>The suffix.</value>
    public string Suffix { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Tag"/> class.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="suffix">The suffix.</param>
    public Tag(string handle, string suffix)
        : this(handle, suffix, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Tag"/> class.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="suffix">The suffix.</param>
    /// <param name="start">The start position of the token.</param>
    /// <param name="end">The end position of the token.</param>
    public Tag(string handle, string suffix, Mark start, Mark end)
        : base(start, end)
    {
        Handle = handle;
        Suffix = suffix;
    }
}