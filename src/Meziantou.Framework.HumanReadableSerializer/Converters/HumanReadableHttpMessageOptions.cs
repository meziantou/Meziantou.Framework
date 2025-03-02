namespace Meziantou.Framework.HumanReadable.Converters;

public record HumanReadableHttpMessageOptions
{
    public ISet<string> ExcludedHeaderNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IList<HttpHeaderValueFormatter> HeaderValueTransformer { get; } = new List<HttpHeaderValueFormatter>();

#if NET5_0_OR_GREATER
    /// <summary>
    /// When <see langword="true" />, the following properties are not serialized:
    ///   <list type="bullet">
    ///    <item><see cref="HttpResponseMessage.Version" /></item>
    ///    <item><see cref="HttpRequestMessage.Version" /></item>
    ///    <item><see cref="HttpRequestMessage.VersionPolicy" /></item>
    ///   </list>
    /// </summary>
#else
    /// <summary>
    /// When <see langword="true" />, the following properties are not serialized:
    ///   <list type="bullet">
    ///    <item><see cref="HttpResponseMessage.Version" /></item>
    ///    <item><see cref="HttpRequestMessage.Version" /></item>
    ///   </list>
    /// </summary>
#endif
    public bool OmitProtocolVersion { get; set; } = true;
}
