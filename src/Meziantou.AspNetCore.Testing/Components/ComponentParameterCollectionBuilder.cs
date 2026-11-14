using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>Sets the parameters of a component, with the names of its properties checked by the compiler.</summary>
/// <typeparam name="TComponent">The type of the component.</typeparam>
public sealed class ComponentParameterCollectionBuilder<TComponent>
    where TComponent : IComponent
{
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);

    internal ComponentParameterCollectionBuilder()
    {
    }

    /// <summary>Sets a parameter.</summary>
    /// <typeparam name="TValue">The type of the parameter.</typeparam>
    /// <param name="parameter">The property of the parameter (<c>c => c.Title</c>).</param>
    /// <param name="value">The value.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> Add<TValue>(Expression<Func<TComponent, TValue>> parameter, TValue value)
    {
        _parameters[GetParameterName(parameter)] = value;
        return this;
    }

    /// <summary>Sets an <see cref="EventCallback"/> parameter.</summary>
    /// <param name="parameter">The property of the parameter (<c>c => c.OnClick</c>).</param>
    /// <param name="callback">The callback.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> Add(Expression<Func<TComponent, EventCallback>> parameter, Action callback)
    {
        _parameters[GetParameterName(parameter)] = new EventCallback(receiver: null, callback);
        return this;
    }

    /// <summary>Sets an <see cref="EventCallback{TValue}"/> parameter.</summary>
    /// <typeparam name="TValue">The type of the argument of the callback.</typeparam>
    /// <param name="parameter">The property of the parameter (<c>c => c.ValueChanged</c>).</param>
    /// <param name="callback">The callback.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> Add<TValue>(Expression<Func<TComponent, EventCallback<TValue>>> parameter, Action<TValue> callback)
    {
        _parameters[GetParameterName(parameter)] = new EventCallback<TValue>(receiver: null, callback);
        return this;
    }

    /// <summary>Sets a <see cref="RenderFragment"/> parameter from markup.</summary>
    /// <param name="parameter">The property of the parameter (<c>c => c.ChildContent</c>).</param>
    /// <param name="markup">The HTML of the fragment.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> Add(Expression<Func<TComponent, RenderFragment?>> parameter, string markup)
    {
        _parameters[GetParameterName(parameter)] = (RenderFragment)(builder => builder.AddMarkupContent(0, markup));
        return this;
    }

    /// <summary>Sets a parameter by name, for instance an attribute captured by <c>CaptureUnmatchedValues</c>.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="value">The value.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> Add(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        _parameters[name] = value;
        return this;
    }

    /// <summary>Sets the <c>ChildContent</c> parameter from markup.</summary>
    /// <param name="markup">The HTML of the content.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> AddChildContent(string markup) => Add("ChildContent", (RenderFragment)(builder => builder.AddMarkupContent(0, markup)));

    /// <summary>Sets the <c>ChildContent</c> parameter.</summary>
    /// <param name="childContent">The content.</param>
    /// <returns>The builder.</returns>
    public ComponentParameterCollectionBuilder<TComponent> AddChildContent(RenderFragment childContent) => Add("ChildContent", childContent);

    internal ParameterView Build() => ParameterView.FromDictionary(_parameters);

    private static string GetParameterName(LambdaExpression expression)
    {
        if (expression.Body is MemberExpression { Member: PropertyInfo property } && (property.IsDefined(typeof(ParameterAttribute)) || property.IsDefined(typeof(CascadingParameterAttribute))))
            return property.Name;

        throw new ArgumentException($"The expression '{expression}' does not select a parameter of {typeof(TComponent).Name} (a property with [Parameter])", nameof(expression));
    }
}
