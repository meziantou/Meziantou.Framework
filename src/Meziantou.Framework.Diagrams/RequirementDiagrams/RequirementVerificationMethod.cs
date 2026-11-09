namespace Meziantou.Framework.Diagrams;

/// <summary>How a requirement is verified, as defined by SysML.</summary>
public enum RequirementVerificationMethod
{
    /// <summary>By analysis.</summary>
    Analysis,

    /// <summary>By inspection.</summary>
    Inspection,

    /// <summary>By test.</summary>
    Test,

    /// <summary>By demonstration.</summary>
    Demonstration,
}
