namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The role of a channel in the decoded image.</summary>
internal enum ExrChannelRole
{
    Red = 0,
    Green = 1,
    Blue = 2,
    Alpha = 3,
    Luminance = 4,

    /// <summary>A channel the decoded image does not use: a channel of another layer, or not a color of the decoded layer.</summary>
    Ignored = 5,

    /// <summary>The <c>Z</c> channel of deep data: the depth of each sample, or the front of its range.</summary>
    Depth = 6,

    /// <summary>The <c>ZBack</c> channel of deep data: the back of the range of depths of each sample.</summary>
    DepthBack = 7,
}
