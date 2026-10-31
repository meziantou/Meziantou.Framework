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
}
