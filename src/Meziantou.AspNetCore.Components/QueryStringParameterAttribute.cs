using System;

namespace Meziantou.AspNetCore.Components;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class QueryStringParameterAttribute : Attribute
{
    public QueryStringParameterAttribute()
    {
    }

    public QueryStringParameterAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Name of the query string parameter. It uses the property name by default.</summary>
    public string? Name { get; }
}
