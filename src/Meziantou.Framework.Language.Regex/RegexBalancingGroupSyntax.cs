namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a .NET balancing group, <c>(?&lt;current-previous&gt;…)</c>.</summary>
public sealed partial class RegexBalancingGroupSyntax : RegexGroupSyntax
{
    /// <summary>The name of the group being pushed, or an empty string when the group only pops.</summary>
    public string Name => NameToken.Text;

    /// <summary>The name or number of the group being popped.</summary>
    public string PreviousName => PreviousNameToken.Text;
}
