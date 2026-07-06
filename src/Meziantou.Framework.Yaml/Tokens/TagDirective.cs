using System.Text.RegularExpressions;

namespace Meziantou.Framework.Yaml.Tokens;

/// <summary>Represents a tag directive token.</summary>
public partial class TagDirective : Token
{
    /// <summary>Gets the handle.</summary>
    /// <value>The handle.</value>
    public string Handle { get; }

    /// <summary>Gets the prefix.</summary>
    /// <value>The prefix.</value>
    public string Prefix { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TagDirective"/> class.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="prefix">The prefix.</param>
    public TagDirective(string handle, string prefix)
        : this(handle, prefix, Mark.Empty, Mark.Empty)
    {
    }

    [GeneratedRegex(@"^!([0-9A-Za-z_\-]*!)?$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: -1)]
    private static partial Regex TagHandleValidator { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TagDirective"/> class.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="prefix">The prefix.</param>
    /// <param name="start">The start position of the token.</param>
    /// <param name="end">The end position of the token.</param>
    public TagDirective(string handle, string prefix, Mark start, Mark end)
        : base(start, end)
    {
        if (string.IsNullOrEmpty(handle))
        {
            throw new ArgumentNullException(nameof(handle), "Tag handle must not be empty.");
        }

        if (!TagHandleValidator.IsMatch(handle))
        {
            throw new ArgumentException("Tag handle must start and end with '!' and contain alphanumerical characters only.", nameof(handle));
        }

        Handle = handle;

        if (string.IsNullOrEmpty(prefix))
        {
            throw new ArgumentNullException(nameof(prefix), "Tag prefix must not be empty.");
        }

        Prefix = prefix;
    }

    /// <summary>Determines whether the specified System.Object is equal to the current System.Object.</summary>
    /// <param name="obj">The System.Object to compare with the current System.Object.</param>
    /// <returns>true if the specified System.Object is equal to the current System.Object; otherwise, false.</returns>
    public override bool Equals(object? obj) => obj is TagDirective other && Handle.Equals(other.Handle, StringComparison.Ordinal) && Prefix.Equals(other.Prefix, StringComparison.Ordinal);

    /// <summary>Serves as a hash function for a particular type.</summary>
    /// <returns>
    /// A hash code for the current <see cref="object"/>.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(Handle, Prefix);

    /// <summary>Returns a string that represents the current object.</summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString() => $"{Handle} => {Prefix}";
}