namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Parses a pattern the way <c>System.Text.RegularExpressions</c> does.</summary>
/// <remarks>
/// Everything is in <see cref="PerlStyleRegexParser"/>, which was ported from the .NET engine in the first place.
/// The .NET flavor is that grammar with nothing taken away.
/// </remarks>
internal sealed class NetRegexParser : PerlStyleRegexParser
{
    public NetRegexParser(string text, RegexParseOptions parseOptions)
        : base(text, parseOptions)
    {
    }

    /// <summary>The deprecated <c>\&lt;name&gt;</c> spelling of a named backreference is .NET's alone.</summary>
    protected override bool AllowsBareAngleBackreference => true;
}
