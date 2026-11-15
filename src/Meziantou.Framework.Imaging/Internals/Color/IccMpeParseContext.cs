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

    public InvalidImageContentException Invalid(string message)
        => new($"The {Role} ICC profile is invalid: in the {IccReader.FormatSignature(TagSignature)} tag, {message}", ImageFormat.Unknown);

    public UnsupportedImageFeatureException Unsupported(string message, string feature)
        => new($"The {Role} ICC profile is not supported: in the {IccReader.FormatSignature(TagSignature)} tag, {message}", ImageFormat.Unknown, feature);
}
