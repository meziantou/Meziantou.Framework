using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The state of the parsing of a <c>multiProcessElementsType</c> tag: the role and the tag for messages, and the depth of nested calculator elements.</summary>
internal sealed class IccMpeParseContext
{
    /// <summary>The largest nesting of calculator elements: a sub-element is smaller than its calculator, so cycles are impossible, but deep nesting would exhaust the call stack.</summary>
    public const int MaxDepth = 16;

    /// <summary>The largest number of operations of one evaluation of a tag (see <see cref="IccMpeElement.Cost"/>).</summary>
    public const long MaxCost = 1 << 20;

    /// <summary>The largest number of channels between two elements and of scratch values of a tag.</summary>
    public const int MaxChannels = 65_535;

    public IccMpeParseContext(string role, uint tagSignature)
    {
        Role = role;
        TagSignature = tagSignature;
    }

    public string Role { get; }

    public uint TagSignature { get; }

    public int Depth { get; set; }

    /// <summary>
    /// Gets or sets the largest number of values the elements of the tag may store: the processing elements may refer to
    /// the same or overlapping data many times, and each reference would otherwise allocate it again.
    /// </summary>
    public long AllocationLimit { get; set; } = long.MaxValue;

    /// <summary>Gets or sets the number of operations spent sampling the calculator curves of the tag when it is read.</summary>
    public long SamplingCost { get; set; }

    /// <summary>Gets the number of values the elements of the tag store.</summary>
    public long Allocated { get; private set; }

    /// <summary>Accounts for the values an element stores, before it allocates them.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The elements of the tag store too many values.</exception>
    public void Allocate(long count)
    {
        Allocated += count;
        if (Allocated > AllocationLimit)
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"the processing elements store more than {AllocationLimit} values, more than their data contain."), "ICC processing elements too large to evaluate");
    }

    public InvalidImageContentException Invalid(string message)
        => new($"The {Role} ICC profile is invalid: in the {IccReader.FormatSignature(TagSignature)} tag, {message}", ImageFormat.Unknown);

    public UnsupportedImageFeatureException Unsupported(string message, string feature)
        => new($"The {Role} ICC profile is not supported: in the {IccReader.FormatSignature(TagSignature)} tag, {message}", ImageFormat.Unknown, feature);
}
