namespace Meziantou.Framework.Json.Internals;

/// <summary>Why <see cref="StreamingEvaluator.Process"/> stopped reading.</summary>
internal enum StreamingStatus
{
    /// <summary>The root value has been read.</summary>
    Completed,

    /// <summary>Matches were found and more tokens remain.</summary>
    MatchesFound,

    /// <summary>The next token is not in the data yet.</summary>
    NeedsMoreData,

    /// <summary>A value to parse is not entirely in the data yet.</summary>
    NeedsWholeValue,
}
