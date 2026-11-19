namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The limits of the XSLT transformations of a browsing context (see <see cref="BrowsingContextOptions.Xslt"/>): the
/// transformations of <see cref="XsltProcessor"/> and of the XML documents whose <c>xml-stylesheet</c> processing instruction
/// references an XSLT stylesheet. They stop the stylesheets that do not end, or that build too large a result.
/// </summary>
/// <remarks>
/// A transformation that exceeds a limit fails: <see cref="XsltProcessor"/> throws a <see cref="DomException"/> whose name is
/// <c>OperationError</c>, and the navigation to an XML document shows the XML document. A transformation run by a script also
/// stops when the script engine interrupts the script (e.g. its timeout): the script is terminated.
/// </remarks>
public sealed record XsltOptions
{
    internal const long DefaultMaxSteps = 100_000_000;
    internal const long DefaultMaxOutputNodes = 2_000_000;

    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Gets the default limits.</summary>
    public static XsltOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the maximum number of steps of a transformation: the instructions it instantiates, the expressions it
    /// evaluates, the patterns it matches and the nodes it copies. The default is 100,000,000.
    /// </summary>
    public long MaxSteps
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultMaxSteps;

    /// <summary>
    /// Gets or sets the maximum number of nodes a transformation creates, in its result and in the result tree fragments of its
    /// variables. The default is 2,000,000.
    /// </summary>
    public long MaxOutputNodes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultMaxOutputNodes;

    /// <summary>
    /// Gets or sets the maximum duration of a transformation, or null for no limit. The default is 10 seconds. The duration is
    /// checked between the steps of the transformation.
    /// </summary>
    public TimeSpan? Timeout
    {
        get;
        init
        {
            if (value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The timeout cannot be negative");

            field = value;
        }
    } = DefaultTimeout;
}
