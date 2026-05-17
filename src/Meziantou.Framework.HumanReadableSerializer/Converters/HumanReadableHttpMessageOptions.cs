namespace Meziantou.Framework.HumanReadable.Converters;

/// <summary>Provides options for serializing HTTP messages.</summary>
public record HumanReadableHttpMessageOptions
{
    /// <summary>Gets the set of header names to exclude from serialization.</summary>
    public ISet<string> ExcludedHeaderNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the list of header value formatters used to transform header values during serialization.</summary>
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
}
