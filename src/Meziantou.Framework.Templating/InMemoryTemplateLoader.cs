using System.Collections;
using System.Collections.Concurrent;

namespace Meziantou.Framework.Templating;

/// <summary>Loads templates from a collection of names and texts held in memory. Names are compared ordinally.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs" ref:region="Template_RenderPartial" ref:dedent="true">
/// var loader = new InMemoryTemplateLoader
/// {
///     { "greeting", "Dear &lt;%= Name %&gt;," },
/// };
///
/// using var template = new Template { TemplateLoader = loader };
/// template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
/// template.Load("&lt;%@ render greeting %&gt; your order has shipped.");
///
/// var result = template.Run("Meziantou");
/// Assert.Equal("Dear Meziantou, your order has shipped.", result);
/// </code>
/// </example>
public sealed class InMemoryTemplateLoader : TemplateLoader, IEnumerable<KeyValuePair<string, string>>
{
    private readonly ConcurrentDictionary<string, string> _templates = new(StringComparer.Ordinal);

    /// <summary>Adds a template, or replaces the text of the template that has the same name.</summary>
    /// <param name="name">The name of the template.</param>
    /// <param name="text">The text of the template.</param>
    public void Add(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(text);

        _templates[name] = text;
    }

    /// <summary>Removes a template.</summary>
    /// <param name="name">The name of the template.</param>
    /// <returns><see langword="true"/> when the template was removed; <see langword="false"/> when no template has this name.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _templates.TryRemove(name, out _);
    }

    /// <inheritdoc/>
    public override string? GetText(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        return _templates.TryGetValue(fullName, out var text) ? text : null;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _templates.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
