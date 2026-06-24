namespace Meziantou.Framework.RobotsTxt;

/// <summary>Specifies whether a <see cref="RobotsRule"/> allows or disallows access to a path.</summary>
public enum RobotsRuleKind
{
    /// <summary>The rule allows access to the matching path.</summary>
    Allow,

    /// <summary>The rule disallows access to the matching path.</summary>
    Disallow,
}
