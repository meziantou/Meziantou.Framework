namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The levels of AV1 (AV1 specification, Annex A.3): the limits that a decoder of a level supports. The encoder announces
/// the lowest level whose picture size limits hold its frames.
/// </summary>
internal static class Av1Levels
{
    /// <summary>The value of <c>seq_level_idx</c> that announces no limit ("maximum parameters").</summary>
    public const int MaximumParameters = 31;

    /// <summary>The value of <c>seq_level_idx</c> of level 5.1.</summary>
    public const int Level51 = 13;

    /// <summary>The value of <c>seq_level_idx</c> of level 6.0.</summary>
    public const int Level60 = 16;

    // seq_level_idx, MaxPicSize, MaxHSize and MaxVSize of the levels that differ by their picture size (table of Annex A.3).
    // The other defined levels (4.1, 5.1 to 5.3, 6.1 to 6.3) only raise the sample and bit rates
    private static ReadOnlySpan<int> PictureLimits =>
    [
        0, 147456, 2048, 1152, // 2.0
        1, 278784, 2816, 1584, // 2.1
        4, 665856, 4352, 2448, // 3.0
        5, 1065024, 5504, 3096, // 3.1
        8, 2359296, 6144, 3456, // 4.0
        12, 8912896, 8192, 4352, // 5.0
        16, 35651584, 16384, 8704, // 6.0
    ];

    /// <summary>
    /// Returns the <c>seq_level_idx</c> of the lowest level whose <c>MaxPicSize</c>, <c>MaxHSize</c> and <c>MaxVSize</c> hold a
    /// frame of <paramref name="width"/> x <paramref name="height"/> samples, or <see cref="MaximumParameters"/> when no level
    /// does.
    /// </summary>
    public static int GetLevelIndex(int width, int height)
    {
        var limits = PictureLimits;
        for (var i = 0; i < limits.Length; i += 4)
        {
            if ((long)width * height <= limits[i + 1] && width <= limits[i + 2] && height <= limits[i + 3])
                return limits[i];
        }

        return MaximumParameters;
    }
}
