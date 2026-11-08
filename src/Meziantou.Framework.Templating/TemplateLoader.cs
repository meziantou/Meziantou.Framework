namespace Meziantou.Framework.Templating;

/// <summary>Finds the text of the partial templates and the layouts that a template renders.</summary>
/// <remarks>
/// A template calls the loader when it is built, once for each distinct template it renders. The partials and the
/// layouts are compiled into the assembly of the template that renders them.
/// </remarks>
public abstract class TemplateLoader
{
    /// <summary>Gets the name that identifies a template rendered by a <c>render</c> or a <c>layout</c> directive.</summary>
    /// <param name="name">The name written in the directive.</param>
    /// <param name="referrerFullName">
    /// The full name of the template that contains the directive, or <see langword="null"/> when the directive is in the
    /// template being built and its <see cref="Template.SourceFileName"/> is not set.
    /// </param>
    /// <returns>
    /// The full name of the template. Two directives that refer to the same template must get the same full name: it
    /// identifies the template, and it is the file name of the compiler diagnostics and of the stack traces.
    /// The default implementation returns <paramref name="name"/>.
    /// </returns>
    public virtual string GetFullName(string name, string? referrerFullName)
    {
        return name;
    }

    /// <summary>Gets the text of a template.</summary>
    /// <param name="fullName">The full name of the template, returned by <see cref="GetFullName"/>.</param>
    /// <returns>The text of the template, or <see langword="null"/> when the template does not exist.</returns>
    public abstract string? GetText(string fullName);
}
