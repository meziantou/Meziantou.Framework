namespace Meziantou.Framework.HumanReadable.Converters;

/// <summary>Provides options for serializing HTTP messages.</summary>
public record HumanReadableHttpMessageOptions
{
    /// <summary>Initializes a new instance of the <see cref="HumanReadableHttpMessageOptions"/> class.</summary>
    public HumanReadableHttpMessageOptions()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HumanReadableHttpMessageOptions"/> class with the values of another instance.</summary>
    /// <param name="original">The instance to copy.</param>
    /// <remarks>The collections are copied, so changing the copy does not change the original instance.</remarks>
    protected HumanReadableHttpMessageOptions(HumanReadableHttpMessageOptions original)
    {
        ArgumentNullException.ThrowIfNull(original);

        // Field initializers are not executed in a record copy constructor, so every property must be copied explicitly
        ExcludedHeaderNames = new HashSet<string>(original.ExcludedHeaderNames, StringComparer.OrdinalIgnoreCase);
        HeaderValueTransformer = [.. original.HeaderValueTransformer];
        OmitProtocolVersion = original.OmitProtocolVersion;
    }

    /// <summary>Gets the set of header names to exclude from serialization.</summary>
    /// <remarks>The names apply to the headers of the message and to the headers of its content.</remarks>
    public ISet<string> ExcludedHeaderNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the list of header value formatters used to transform header values during serialization.</summary>
    /// <remarks>The formatters apply to the headers of the message and to the headers of its content.</remarks>
    public IList<HttpHeaderValueFormatter> HeaderValueTransformer { get; } = new List<HttpHeaderValueFormatter>();

    /// <summary>
    /// When <see langword="true" />, the following properties are not serialized:
    ///   <list type="bullet">
    ///    <item><see cref="HttpResponseMessage.Version" /></item>
    ///    <item><see cref="HttpRequestMessage.Version" /></item>
    ///    <item><see cref="HttpRequestMessage.VersionPolicy" /></item>
    ///   </list>
    /// </summary>
    public bool OmitProtocolVersion { get; set; } = true;

    /// <summary>Determines whether the specified options are equal to the current options, comparing the content of the collections.</summary>
    /// <param name="other">The options to compare with.</param>
    /// <returns><see langword="true"/> if the options are equal; otherwise, <see langword="false"/>.</returns>
    public virtual bool Equals([NotNullWhen(true)] HumanReadableHttpMessageOptions? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        return other is not null
            && EqualityContract == other.EqualityContract
            && OmitProtocolVersion == other.OmitProtocolVersion
            && ExcludedHeaderNames.SetEquals(other.ExcludedHeaderNames)
            && HeaderValueTransformer.SequenceEqual(other.HeaderValueTransformer);
    }

    public override int GetHashCode()
    {
        // The hash code must not depend on the order of the set
        return HashCode.Combine(EqualityContract, OmitProtocolVersion, ExcludedHeaderNames.Count, HeaderValueTransformer.Count);
    }
}
