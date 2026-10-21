namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The internal slots of %RegExp% of the legacy RegExp features (https://github.com/tc39/proposal-regexp-legacy-features):
/// [[RegExpInput]], [[RegExpLastMatch]], [[RegExpLastParen]], [[RegExpLeftContext]], [[RegExpRightContext]] and
/// [[RegExpParen1-9]]. The values derived from the last match are computed when they are read.
/// </summary>
internal sealed class RegExpLegacyStaticProperties
{
    // null: the slot is empty (InvalidateLegacyRegExpStaticProperties)
    private string? _input = "";
    private string? _matchInput = "";
    private int[]? _captures;

    /// <summary>UpdateLegacyRegExpStaticProperties(%RegExp%, S, startIndex, endIndex, capturedValues)</summary>
    /// <param name="input">S</param>
    /// <param name="captures">The capture slots of the match: the start index, the end index, then the groups.</param>
    public void Update(string input, int[] captures)
    {
        _input = input;
        _matchInput = input;
        _captures = captures;
    }

    /// <summary>InvalidateLegacyRegExpStaticProperties(%RegExp%)</summary>
    public void Invalidate()
    {
        _input = null;
        _matchInput = null;
        _captures = null;
    }

    /// <summary>[[RegExpInput]], or null when it is empty.</summary>
    public string? Input
    {
        get => _input;
        set => _input = value;
    }

    /// <summary>Gets [[RegExpLastMatch]], or null when it is empty.</summary>
    public string? LastMatch => _matchInput is null ? null : _captures is null ? "" : _matchInput[_captures[0].._captures[1]];

    /// <summary>Gets [[RegExpLastParen]], or null when it is empty.</summary>
    public string? LastParen
    {
        get
        {
            if (_matchInput is null)
                return null;

            if (_captures is null || _captures.Length <= 2)
                return "";

            return GetCapture((_captures.Length / 2) - 1);
        }
    }

    /// <summary>Gets [[RegExpLeftContext]], or null when it is empty.</summary>
    public string? LeftContext => _matchInput is null ? null : _captures is null ? "" : _matchInput[.._captures[0]];

    /// <summary>Gets [[RegExpRightContext]], or null when it is empty.</summary>
    public string? RightContext => _matchInput is null ? null : _captures is null ? "" : _matchInput[_captures[1]..];

    /// <summary>Gets [[RegExpParen<paramref name="index"/>]] (1 to 9), or null when it is empty.</summary>
    public string? GetParen(int index)
    {
        if (_matchInput is null)
            return null;

        if (_captures is null || index >= _captures.Length / 2)
            return "";

        return GetCapture(index);
    }

    private string GetCapture(int index)
    {
        var start = _captures![index * 2];
        var end = _captures[(index * 2) + 1];
        return start < 0 || end < 0 ? "" : _matchInput![start..end];
    }
}
