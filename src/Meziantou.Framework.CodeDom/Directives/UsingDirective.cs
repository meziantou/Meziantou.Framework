namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a using directive for importing namespaces.</summary>
public class UsingDirective : Directive
{
    public UsingDirective()
        : this(ns: null)
    {
    }

    public UsingDirective(string? ns)
    {
        Namespace = ns;
    }

    public string? Namespace { get; set; }
}
