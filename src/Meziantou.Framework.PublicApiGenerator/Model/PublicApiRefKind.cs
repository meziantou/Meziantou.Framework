namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>How a parameter, a return value or a field is passed.</summary>
public enum PublicApiRefKind
{
    None,
    Ref,
    Out,
    In,
    RefReadOnly,
}
